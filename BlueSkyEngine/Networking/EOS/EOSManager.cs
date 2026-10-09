using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text;

namespace BlueSky.Networking.EOS;

/// <summary>
/// High-level EOS SDK manager.
/// Handles Platform init → Auth login → Connect login → P2P transport lifecycle.
/// All heavy lifting is delegated to the native SDK via P/Invoke.
/// </summary>
public sealed class EOSManager : IDisposable
{
    // ── Handles ──────────────────────────────────────────────────────
    private EOS_HPlatform _platform;
    private EOS_HAuth _auth;
    private EOS_HConnect _connect;
    private EOS_HP2P _p2p;
    private EOS_HUserInfo _userInfo;
    private bool _initialized;
    private bool _disposed;

    // ── State ────────────────────────────────────────────────────────
    public bool IsInitialized => _initialized;
    public bool IsAuthLoggedIn { get; private set; }
    public bool IsConnectLoggedIn { get; private set; }
    public EOS_EpicAccountId LocalEpicAccountId { get; private set; }
    public EOS_ProductUserId LocalProductUserId { get; private set; }
    public string DisplayName { get; private set; } = "";
    public EosCredentials Credentials { get; private set; } = null!;

    // ── P2P state ────────────────────────────────────────────────────
    private EOS_NotificationId _connectionRequestNotificationId;
    private EOS_NotificationId _connectionEstablishedNotificationId = default;
    private readonly List<EOS_ProductUserId> _connectedPeers = new();
    private readonly object _peerLock = new();

    // ── Callback refs (prevent GC) ───────────────────────────────────
    private EOS_Auth_OnLoginCallback? _authLoginCallback;
    private EOS_Connect_OnLoginCallback? _connectLoginCallback;
    private EOS_Connect_OnCreateUserCallback? _connectCreateUserCallback;
    private EOS_P2P_OnIncomingConnectionRequestCallback? _p2pConnectionRequestCallback;
    private EOS_UserInfo_OnQueryUserInfoCallback? _userInfoCallback;
    private EOS_LogMessageFunc? _logMessageCallback;

    // ── Logging ──────────────────────────────────────────────────────
    private Action<string>? _log;
    public Action<string>? OnPeerConnected { get; set; }
    public Action<string, byte[], byte>? OnPacketReceived { get; set; }

    // ═══════════════════════════════════════════════════════════════
    //  Platform Lifecycle
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Initialize the EOS SDK and create the platform instance.
    /// Must be called before any other EOS operations.
    /// </summary>
    public bool Initialize(EosCredentials credentials, Action<string>? log = null)
    {
        _log = log;
        Credentials = credentials;

        if (!credentials.IsConfigured)
        {
            log?.Invoke("[EOS] Credentials not configured — skipping initialization");
            return false;
        }

        log?.Invoke("[EOS] Initializing EOS SDK...");

        // Step 1: EOS_Initialize
        var productName = Marshal.StringToHGlobalAnsi(credentials.ProductName);
        var productVersion = Marshal.StringToHGlobalAnsi(credentials.ProductVersion);

        var initOptions = new EOS_InitializeOptions
        {
            ApiVersion = EOS_Constants.EOS_INITIALIZE_API_LATEST,
            ProductName = productName,
            ProductVersion = productVersion,
        };

        EOS_EResult initResult = EosNativeLib.EOS_Initialize(ref initOptions);
        Marshal.FreeHGlobal(productName);
        Marshal.FreeHGlobal(productVersion);

        if (initResult != EOS_EResult.Success)
        {
            log?.Invoke($"[EOS] EOS_Initialize failed: {initResult}");
            return false;
        }
        log?.Invoke("[EOS] SDK initialized successfully");

        // Register SDK Logging
        _logMessageCallback = (ref EOS_LogMessage message) =>
        {
            string category = Marshal.PtrToStringUTF8(message.Category) ?? "";
            string msg = Marshal.PtrToStringUTF8(message.Message) ?? "";
            log?.Invoke($"[EOS Native SDK] [{message.Level}] [{category}] {msg}");
        };
        EosNativeLib.EOS_Logging_SetCallback(_logMessageCallback);
        EosNativeLib.EOS_Logging_SetLogLevel(EOS_ELogCategory.AllCategories, EOS_ELogLevel.VeryVerbose);

        // Step 2: EOS_Platform_Create
        var productId = Marshal.StringToHGlobalAnsi(credentials.ProductId);
        var sandboxId = Marshal.StringToHGlobalAnsi(credentials.SandboxId);
        var deploymentId = Marshal.StringToHGlobalAnsi(credentials.DeploymentId);
        var clientId = Marshal.StringToHGlobalAnsi(credentials.ClientId);
        var clientSecret = Marshal.StringToHGlobalAnsi(credentials.ClientSecret);
        var cacheDir = Marshal.StringToHGlobalAnsi(
            System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "EOSCache"));

        var platformOptions = new EOS_Platform_Options
        {
            ApiVersion = EOS_Constants.EOS_PLATFORM_OPTIONS_API_LATEST,
            ProductId = productId,
            SandboxId = sandboxId,
            DeploymentId = deploymentId,
            ClientCredentials = new EOS_Platform_ClientCredentials
            {
                ClientId = clientId,
                ClientSecret = clientSecret,
            },
            bIsServer = EOS_Constants.EOS_FALSE,
            Flags = EOS_Constants.EOS_PF_DISABLE_OVERLAY, // No overlay needed for engine
            CacheDirectory = cacheDir,
            TickBudgetInMilliseconds = 0, // Process all available work
        };

        _platform = EosNativeLib.EOS_Platform_Create(ref platformOptions);

        Marshal.FreeHGlobal(productId);
        Marshal.FreeHGlobal(sandboxId);
        Marshal.FreeHGlobal(deploymentId);
        Marshal.FreeHGlobal(clientId);
        Marshal.FreeHGlobal(clientSecret);
        Marshal.FreeHGlobal(cacheDir);

        if (!_platform.Handle.IsValid())
        {
            log?.Invoke("[EOS] EOS_Platform_Create returned null handle");
            return false;
        }

        // Step 3: Get interface handles
        _auth = EosNativeLib.EOS_Platform_GetAuthInterface(_platform);
        _connect = EosNativeLib.EOS_Platform_GetConnectInterface(_platform);
        _p2p = EosNativeLib.EOS_Platform_GetP2PInterface(_platform);
        _userInfo = EosNativeLib.EOS_Platform_GetUserInfoInterface(_platform);

        log?.Invoke($"[EOS] Platform created — Auth={_auth.Handle.IsValid()}, Connect={_connect.Handle.IsValid()}, P2P={_p2p.Handle.IsValid()}, UserInfo={_userInfo.Handle.IsValid()}");

        _initialized = true;
        return true;
    }

    /// <summary>
    /// Tick the EOS platform — must be called every frame to pump callbacks.
    /// </summary>
    public void Tick()
    {
        if (!_initialized || !_platform.Handle.IsValid())
            return;

        EosNativeLib.EOS_Platform_Tick(_platform);
        RefreshLoginStatusFromSdk();
    }

    private void RefreshLoginStatusFromSdk()
    {
        if (!_initialized || !_auth.Handle.IsValid())
            return;

        if (LocalEpicAccountId.Handle == IntPtr.Zero)
            return;

        var status = EosNativeLib.EOS_Auth_GetLoginStatus(_auth, LocalEpicAccountId);
        IsAuthLoggedIn = status == EOS_ELoginStatus.LoggedIn;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Auth Login (Epic Account)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Login using Epic Account Portal (opens browser for user to authorize).
    /// This is the standard flow for standalone applications.
    /// </summary>
    public void LoginWithAccountPortal(Action<bool, string>? onComplete = null)
    {
        if (!_initialized)
        {
            onComplete?.Invoke(false, "EOS not initialized");
            return;
        }

        _log?.Invoke("[EOS] Starting Auth login via Account Portal...");

        var credentialsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<EOS_Auth_Credentials>());
        var credentials = new EOS_Auth_Credentials
        {
            ApiVersion = EOS_Constants.EOS_AUTH_CREDENTIALS_API_LATEST,
            Type = EOS_ELoginCredentialType.AccountPortal,
            Token = IntPtr.Zero,
        };
        Marshal.StructureToPtr(credentials, credentialsPtr, false);

        var loginOptionsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<EOS_Auth_LoginOptions>());
        var loginOptions = new EOS_Auth_LoginOptions
        {
            ApiVersion = EOS_Constants.EOS_AUTH_LOGIN_API_LATEST,
            Credentials = credentialsPtr,
            ScopeFlags = EOS_EAuthScopeFlags.NoFlags,
        };
        Marshal.StructureToPtr(loginOptions, loginOptionsPtr, false);

        // Pin callback to prevent GC
        _authLoginCallback = (ref EOS_Auth_LoginCallbackInfo data) =>
        {
            _log?.Invoke($"[EOS] Auth login callback: {data.ResultCode}");

            bool isComplete = EosNativeLib.EOS_EResult_IsOperationComplete(data.ResultCode) == EOS_Constants.EOS_TRUE;

            if (data.ResultCode == EOS_EResult.Success)
            {
                LocalEpicAccountId = data.LocalUserId;
                IsAuthLoggedIn = true;
                _log?.Invoke($"[EOS] Auth login success — EpicAccountId valid={EosNativeLib.EOS_EpicAccountId_IsValid(data.LocalUserId) == EOS_Constants.EOS_TRUE}");

                // Fetch display name from UserInfo API
                FetchDisplayName(data.LocalUserId);

                // Now do Connect login with the auth token
                LoginConnectWithPersistentAuth(onComplete);
            }
            else if (!isComplete || data.ResultCode == EOS_EResult.AlreadyPending)
            {
                _log?.Invoke($"[EOS] Auth login still pending: {data.ResultCode}");
                // Keep the account-portal flow alive. EOS will call this callback again when it finishes.
                // Do not call onComplete here, or the UI may stay stuck in LoggingIn.
                return;
            }
            else
            {
                _log?.Invoke($"[EOS] Auth login failed: {data.ResultCode}");
                onComplete?.Invoke(false, $"Auth login failed: {data.ResultCode}");
            }

            // Free marshaled memory
            Marshal.FreeHGlobal(credentialsPtr);
            Marshal.FreeHGlobal(loginOptionsPtr);
        };

        EosNativeLib.EOS_Auth_Login(_auth, loginOptionsPtr, IntPtr.Zero, _authLoginCallback);
    }

    /// <summary>
    /// Login using a Developer token (for testing with the DevAuth tool).
    /// </summary>
    public void LoginWithDeveloperToken(string devToken, Action<bool, string>? onComplete = null)
    {
        if (!_initialized)
        {
            onComplete?.Invoke(false, "EOS not initialized");
            return;
        }

        _log?.Invoke("[EOS] Starting Auth login via Developer token...");

        var tokenPtr = Marshal.StringToHGlobalAnsi(devToken);
        var credentialsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<EOS_Auth_Credentials>());
        var credentials = new EOS_Auth_Credentials
        {
            ApiVersion = EOS_Constants.EOS_AUTH_CREDENTIALS_API_LATEST,
            Type = EOS_ELoginCredentialType.Developer,
            Token = tokenPtr,
        };
        Marshal.StructureToPtr(credentials, credentialsPtr, false);

        var loginOptionsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<EOS_Auth_LoginOptions>());
        var loginOptions = new EOS_Auth_LoginOptions
        {
            ApiVersion = EOS_Constants.EOS_AUTH_LOGIN_API_LATEST,
            Credentials = credentialsPtr,
            ScopeFlags = EOS_EAuthScopeFlags.NoFlags,
        };
        Marshal.StructureToPtr(loginOptions, loginOptionsPtr, false);

        _authLoginCallback = (ref EOS_Auth_LoginCallbackInfo data) =>
        {
            _log?.Invoke($"[EOS] Auth login callback: {data.ResultCode}");

            bool isComplete = EosNativeLib.EOS_EResult_IsOperationComplete(data.ResultCode) == EOS_Constants.EOS_TRUE;

            if (data.ResultCode == EOS_EResult.Success)
            {
                LocalEpicAccountId = data.LocalUserId;
                IsAuthLoggedIn = true;

                // Fetch display name from UserInfo API
                FetchDisplayName(data.LocalUserId);

                LoginConnectWithPersistentAuth(onComplete);
            }
            else if (!isComplete || data.ResultCode == EOS_EResult.AlreadyPending)
            {
                _log?.Invoke($"[EOS] Auth login still pending: {data.ResultCode}");
                return;
            }
            else
            {
                _log?.Invoke($"[EOS] Auth login failed: {data.ResultCode}");
                onComplete?.Invoke(false, $"Auth login failed: {data.ResultCode}");
            }

            Marshal.FreeHGlobal(tokenPtr);
            Marshal.FreeHGlobal(credentialsPtr);
            Marshal.FreeHGlobal(loginOptionsPtr);
        };

        EosNativeLib.EOS_Auth_Login(_auth, loginOptionsPtr, IntPtr.Zero, _authLoginCallback);
    }

    // ═══════════════════════════════════════════════════════════════
    //  User Info (Display Name)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Fetch the Epic Account display name using the UserInfo API.
    /// This is an async query — the callback fires when the backend responds.
    /// </summary>
    private void FetchDisplayName(EOS_EpicAccountId accountId)
    {
        if (!_initialized || !_userInfo.Handle.IsValid())
        {
            _log?.Invoke("[EOS] UserInfo not available — skipping display name fetch");
            return;
        }

        _log?.Invoke($"[EOS] Fetching display name for account...");

        var queryOpts = new EOS_UserInfo_QueryUserInfoOptions
        {
            ApiVersion = 1, // EOS_USERINFO_QUERYUSERINFO_API_LATEST
            LocalUserId = accountId,
            TargetUserId = accountId, // Querying our own info
        };
        var queryOptsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<EOS_UserInfo_QueryUserInfoOptions>());
        Marshal.StructureToPtr(queryOpts, queryOptsPtr, false);

        _userInfoCallback = (ref EOS_UserInfo_QueryUserInfoCallbackInfo data) =>
        {
            Marshal.FreeHGlobal(queryOptsPtr);

            if (data.ResultCode != EOS_EResult.Success)
            {
                _log?.Invoke($"[EOS] QueryUserInfo failed: {data.ResultCode}");
                return;
            }

            // Now copy the cached user info
            var copyOpts = new EOS_UserInfo_CopyUserInfoOptions
            {
                ApiVersion = 3, // EOS_USERINFO_COPYUSERINFO_API_LATEST
                LocalUserId = data.LocalUserId,
                TargetUserId = data.TargetUserId,
            };
            var copyOptsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<EOS_UserInfo_CopyUserInfoOptions>());
            Marshal.StructureToPtr(copyOpts, copyOptsPtr, false);

            EOS_EResult copyResult = EosNativeLib.EOS_UserInfo_CopyUserInfo(_userInfo, copyOptsPtr, out IntPtr userInfoPtr);
            Marshal.FreeHGlobal(copyOptsPtr);

            if (copyResult == EOS_EResult.Success && userInfoPtr != IntPtr.Zero)
            {
                var userInfo = Marshal.PtrToStructure<EOS_UserInfo>(userInfoPtr);
                string displayName = Marshal.PtrToStringUTF8(userInfo.DisplayName) ?? "Unknown";
                EosNativeLib.EOS_UserInfo_Release(userInfoPtr);

                DisplayName = displayName;
                _log?.Invoke($"[EOS] Display name: {displayName}");
            }
            else
            {
                _log?.Invoke($"[EOS] CopyUserInfo failed: {copyResult}");
            }
        };

        EosNativeLib.EOS_UserInfo_QueryUserInfo(_userInfo, queryOptsPtr, IntPtr.Zero, _userInfoCallback);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Connect Login (Product User)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// After successful Auth login, create a Product User via Connect.
    /// Uses the Epic Account Token from Auth to authenticate with Connect.
    /// </summary>
    private void LoginConnectWithPersistentAuth(Action<bool, string>? onComplete)
    {
        _log?.Invoke("[EOS] Creating Connect Product User...");

        // First, copy the auth token to get the access token
        var copyTokenOpts = new EOS_Auth_CopyUserAuthTokenOptions
        {
            ApiVersion = EOS_Constants.EOS_AUTH_COPYAUTHTOKEN_API_LATEST,
        };
        var copyTokenOptsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<EOS_Auth_CopyUserAuthTokenOptions>());
        Marshal.StructureToPtr(copyTokenOpts, copyTokenOptsPtr, false);

        EOS_EResult tokenResult = EosNativeLib.EOS_Auth_CopyUserAuthToken(
            _auth, copyTokenOptsPtr, LocalEpicAccountId, out IntPtr authTokenPtr);
        Marshal.FreeHGlobal(copyTokenOptsPtr);

        if (tokenResult != EOS_EResult.Success || authTokenPtr == IntPtr.Zero)
        {
            _log?.Invoke($"[EOS] Failed to copy auth token: {tokenResult}");
            onComplete?.Invoke(false, $"Failed to copy auth token: {tokenResult}");
            return;
        }

        // Read the token struct to get the access token string
        var authToken = Marshal.PtrToStructure<EOS_Auth_Token>(authTokenPtr);
        string accessToken = Marshal.PtrToStringUTF8(authToken.AccessToken) ?? "";

        // Release the auth token
        EosNativeLib.EOS_Auth_Token_Release(authTokenPtr);

        if (string.IsNullOrEmpty(accessToken))
        {
            _log?.Invoke("[EOS] Auth token access token is empty");
            onComplete?.Invoke(false, "Auth token access token is empty");
            return;
        }

        // Now login to Connect using the Epic Account access token
        var tokenStrPtr = Marshal.StringToHGlobalAnsi(accessToken);
        var connectCredsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<EOS_Connect_Credentials>());
        var connectCreds = new EOS_Connect_Credentials
        {
            ApiVersion = EOS_Constants.EOS_CONNECT_CREDENTIALS_API_LATEST,
            Token = tokenStrPtr,
            Type = EOS_EExternalCredentialType.Epic,
        };
        Marshal.StructureToPtr(connectCreds, connectCredsPtr, false);

        var connectLoginOptsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<EOS_Connect_LoginOptions>());
        var connectLoginOpts = new EOS_Connect_LoginOptions
        {
            ApiVersion = EOS_Constants.EOS_CONNECT_LOGIN_API_LATEST,
            Credentials = connectCredsPtr,
            UserLoginInfo = IntPtr.Zero,
        };
        Marshal.StructureToPtr(connectLoginOpts, connectLoginOptsPtr, false);

        _connectLoginCallback = (ref EOS_Connect_LoginCallbackInfo data) =>
        {
            _log?.Invoke($"[EOS] Connect login callback: {data.ResultCode}");

            if (data.ResultCode == EOS_EResult.Success)
            {
                LocalProductUserId = data.LocalUserId;
                IsConnectLoggedIn = true;
                _log?.Invoke($"[EOS] Connect login success — ProductUserId valid={EosNativeLib.EOS_ProductUserId_IsValid(data.LocalUserId) == EOS_Constants.EOS_TRUE}");

                // Setup P2P
                SetupP2P();

                onComplete?.Invoke(true, "Login successful");
            }
            else if (data.ResultCode == EOS_EResult.InvalidUser && data.ContinuanceToken != IntPtr.Zero)
            {
                _log?.Invoke("[EOS] Connect: User not found — creating new Product User...");
                CreateUserFromContinuanceToken(data.ContinuanceToken, onComplete);
            }
            else
            {
                _log?.Invoke($"[EOS] Connect login failed: {data.ResultCode}");
                onComplete?.Invoke(false, $"Connect login failed: {data.ResultCode}");
            }

            Marshal.FreeHGlobal(tokenStrPtr);
            Marshal.FreeHGlobal(connectCredsPtr);
            Marshal.FreeHGlobal(connectLoginOptsPtr);
        };

        EosNativeLib.EOS_Connect_Login(_connect, connectLoginOptsPtr, IntPtr.Zero, _connectLoginCallback);
    }

    /// <summary>
    /// Create a new Product User from a continuance token (first-time user).
    /// </summary>
    private void CreateUserFromContinuanceToken(IntPtr continuanceToken, Action<bool, string>? onComplete)
    {
        var createUserOptsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<EOS_Connect_CreateUserOptions>());
        var createUserOpts = new EOS_Connect_CreateUserOptions
        {
            ApiVersion = EOS_Constants.EOS_CONNECT_CREATEUSER_API_LATEST,
            ContinuanceToken = continuanceToken,
        };
        Marshal.StructureToPtr(createUserOpts, createUserOptsPtr, false);

        _connectCreateUserCallback = (ref EOS_Connect_CreateUserCallbackInfo data) =>
        {
            _log?.Invoke($"[EOS] Connect CreateUser callback: {data.ResultCode}");

            if (data.ResultCode == EOS_EResult.Success)
            {
                LocalProductUserId = data.LocalUserId;
                IsConnectLoggedIn = true;
                _log?.Invoke($"[EOS] Product User created — ID valid={EosNativeLib.EOS_ProductUserId_IsValid(data.LocalUserId) == EOS_Constants.EOS_TRUE}");

                SetupP2P();
                onComplete?.Invoke(true, "Login successful (new user created)");
            }
            else
            {
                _log?.Invoke($"[EOS] CreateUser failed: {data.ResultCode}");
                onComplete?.Invoke(false, $"CreateUser failed: {data.ResultCode}");
            }

            Marshal.FreeHGlobal(createUserOptsPtr);
        };

        EosNativeLib.EOS_Connect_CreateUser(_connect, createUserOptsPtr, IntPtr.Zero, _connectCreateUserCallback);
    }

    // ═══════════════════════════════════════════════════════════════
    //  P2P Transport
    // ═══════════════════════════════════════════════════════════════

    private void SetupP2P()
    {
        if (!_initialized || !_p2p.Handle.IsValid())
            return;

        _log?.Invoke("[EOS] Setting up P2P notification listeners...");

        // Listen for incoming connection requests (auto-accept)
        _p2pConnectionRequestCallback = (ref EOS_P2P_OnIncomingConnectionRequestInfo info) =>
        {
            _log?.Invoke($"[EOS] P2P connection request from peer (ProductUserId handle valid={info.RemoteUserId.Handle != IntPtr.Zero})");
            AcceptConnection(info.RemoteUserId);
        };

        var notifOpts = new EOS_P2P_AddNotifyPeerConnectionRequestOptions
        {
            ApiVersion = EOS_Constants.EOS_P2P_ADDNOTIFYPEERCONNECTIONREQUEST_API_LATEST,
            LocalUserId = IntPtr.Zero, // NULL = all local users
            SocketId = IntPtr.Zero,    // NULL = all socket IDs
        };

        _connectionRequestNotificationId = EosNativeLib.EOS_P2P_AddNotifyPeerConnectionRequest(
            _p2p, ref notifOpts, IntPtr.Zero, _p2pConnectionRequestCallback);

        if (_connectionRequestNotificationId.IsValid)
        {
            _log?.Invoke($"[EOS] P2P connection request listener registered (ID={_connectionRequestNotificationId.Id})");
        }

        _log?.Invoke("[EOS] P2P setup complete");
    }

    /// <summary>
    /// Proactively open a P2P connection to a remote peer.
    /// Call this when a player joins the lobby to ensure the P2P channel is ready.
    /// EOS P2P has no explicit open-connection call; sending a packet initiates the connection.
    /// We send a small handshake byte to trigger the implicit connection setup.
    /// </summary>
    public void OpenConnection(EOS_ProductUserId remoteUserId)
    {
        if (!_initialized || !_p2p.Handle.IsValid() || !IsConnectLoggedIn)
            return;

        if (EosNativeLib.EOS_ProductUserId_IsValid(remoteUserId) != EOS_Constants.EOS_TRUE)
        {
            _log?.Invoke($"[EOS] OpenConnection: invalid RemoteUserId (handle={remoteUserId.Handle})");
            return;
        }

        // EOS P2P has no explicit OpenConnection API.
        // Sending a packet triggers the implicit connection handshake.
        // Send a 1-byte handshake marker on channel 0 (reliable).
        byte[] handshake = new byte[] { 0x01 }; // 1 = P2P handshake
        bool sent = SendPacket(remoteUserId, handshake, channel: 0, EOS_EPacketReliability.ReliableOrdered);

        _log?.Invoke($"[EOS] OpenConnection via handshake to peer (handle={remoteUserId.Handle}): {(sent ? "OK" : "FAILED")}");

        if (sent)
        {
            lock (_peerLock)
            {
                if (!_connectedPeers.Contains(remoteUserId))
                    _connectedPeers.Add(remoteUserId);
            }
        }
    }

    /// <summary>
    /// Accept an incoming P2P connection from a remote peer.
    /// </summary>
    public void AcceptConnection(EOS_ProductUserId remoteUserId)
    {
        if (!_initialized || !_p2p.Handle.IsValid())
            return;

        // Create a socket ID for the connection
        var socketIdPtr = Marshal.AllocHGlobal(Marshal.SizeOf<EOS_P2P_SocketId>());
        var socketId = new EOS_P2P_SocketId
        {
            ApiVersion = EOS_Constants.EOS_P2P_SOCKETID_API_LATEST,
            SocketName = "BlueSkyDefault",
        };
        Marshal.StructureToPtr(socketId, socketIdPtr, false);

        var acceptOpts = new EOS_P2P_AcceptConnectionOptions
        {
            ApiVersion = EOS_Constants.EOS_P2P_ACCEPTCONNECTION_API_LATEST,
            LocalUserId = LocalProductUserId,
            RemoteUserId = remoteUserId,
            SocketId = socketIdPtr,
        };

        EOS_EResult result = EosNativeLib.EOS_P2P_AcceptConnection(_p2p, ref acceptOpts);
        Marshal.FreeHGlobal(socketIdPtr);

        _log?.Invoke($"[EOS] AcceptConnection result: {result}");

        if (result == EOS_EResult.Success)
        {
            lock (_peerLock)
            {
                if (!_connectedPeers.Contains(remoteUserId))
                    _connectedPeers.Add(remoteUserId);
            }
            OnPeerConnected?.Invoke($"Peer connected (handle={remoteUserId.Handle})");
        }
    }

    /// <summary>
    /// Close all P2P connections on the default socket.
    /// Call before ForceReconnect to clear stale session IDs.
    /// </summary>
    public void CloseAllP2PConnections()
    {
        if (!_initialized || !_p2p.Handle.IsValid() || !IsConnectLoggedIn)
            return;

        var socketIdPtr = Marshal.AllocHGlobal(Marshal.SizeOf<EOS_P2P_SocketId>());
        var socketId = new EOS_P2P_SocketId
        {
            ApiVersion = EOS_Constants.EOS_P2P_SOCKETID_API_LATEST,
            SocketName = "BlueSkyDefault",
        };
        Marshal.StructureToPtr(socketId, socketIdPtr, false);

        var closeOpts = new EOS_P2P_CloseConnectionsOptions
        {
            ApiVersion = 1,
            LocalUserId = LocalProductUserId,
            SocketId = socketIdPtr,
        };

        EOS_EResult result = EosNativeLib.EOS_P2P_CloseConnections(_p2p, ref closeOpts);
        _log?.Invoke($"[EOS] CloseAllP2PConnections result: {result}");

        Marshal.FreeHGlobal(socketIdPtr);

        lock (_peerLock)
        {
            _connectedPeers.Clear();
        }
    }

    /// <summary>
    /// Send data to a connected peer over P2P.
    /// </summary>
    public bool SendPacket(EOS_ProductUserId remoteUserId, byte[] data, byte channel = 0,
                           EOS_EPacketReliability reliability = EOS_EPacketReliability.ReliableOrdered)
    {
        if (!_initialized || !_p2p.Handle.IsValid() || !IsConnectLoggedIn)
            return false;

        if (data == null || data.Length == 0 || data.Length > EOS_Constants.EOS_P2P_MAX_PACKET_SIZE)
            return false;

        var socketIdPtr = Marshal.AllocHGlobal(Marshal.SizeOf<EOS_P2P_SocketId>());
        var socketId = new EOS_P2P_SocketId
        {
            ApiVersion = EOS_Constants.EOS_P2P_SOCKETID_API_LATEST,
            SocketName = "BlueSkyDefault",
        };
        Marshal.StructureToPtr(socketId, socketIdPtr, false);

        var dataPtr = Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data, 0, dataPtr, data.Length);

        var sendOpts = new EOS_P2P_SendPacketOptions
        {
            ApiVersion = EOS_Constants.EOS_P2P_SENDPACKET_API_LATEST,
            LocalUserId = LocalProductUserId,
            RemoteUserId = remoteUserId,
            SocketId = socketIdPtr,
            Channel = channel,
            DataLengthBytes = (uint)data.Length,
            Data = dataPtr,
            bAllowDelayedDelivery = EOS_Constants.EOS_TRUE,
            Reliability = reliability,
            bDisableAutoAcceptConnection = EOS_Constants.EOS_FALSE,
        };

        EOS_EResult result = EosNativeLib.EOS_P2P_SendPacket(_p2p, ref sendOpts);

        Marshal.FreeHGlobal(dataPtr);
        Marshal.FreeHGlobal(socketIdPtr);

        if (result != EOS_EResult.Success)
        {
            _log?.Invoke($"[EOS] SendPacket FAILED to peer (handle={remoteUserId.Handle}, channel={channel}): {result}");
        }
        else
        {
            _log?.Invoke($"[EOS] SendPacket OK to peer (handle={remoteUserId.Handle}, channel={channel}, {data.Length} bytes)");
        }

        return result == EOS_EResult.Success;
    }

    /// <summary>
    /// Receive all pending packets from peers. Call this during Tick().
    /// </summary>
    public void ReceivePackets()
    {
        if (!_initialized || !_p2p.Handle.IsValid() || !IsConnectLoggedIn)
            return;

        while (true)
        {
            var sizeOpts = new EOS_P2P_GetNextReceivedPacketSizeOptions
            {
                ApiVersion = EOS_Constants.EOS_P2P_GETNEXTRECEIVEDPACKETSIZE_API_LATEST,
                LocalUserId = LocalProductUserId,
            };

            EOS_EResult sizeResult = EosNativeLib.EOS_P2P_GetNextReceivedPacketSize(
                _p2p, ref sizeOpts, out uint packetSize);

            if (sizeResult != EOS_EResult.Success || packetSize == 0)
                break;

            if (packetSize > EOS_Constants.EOS_P2P_MAX_PACKET_SIZE)
                break;

            var dataBuffer = Marshal.AllocHGlobal((int)packetSize);
            var socketIdPtr = Marshal.AllocHGlobal(Marshal.SizeOf<EOS_P2P_SocketId>());

            var receiveOpts = new EOS_P2P_ReceivePacketOptions
            {
                ApiVersion = EOS_Constants.EOS_P2P_RECEIVEPACKET_API_LATEST,
                LocalUserId = LocalProductUserId,
                MaxDataSizeBytes = packetSize,
                RequestedChannel = IntPtr.Zero,
            };

            EOS_EResult recvResult = EosNativeLib.EOS_P2P_ReceivePacket(
                _p2p, ref receiveOpts, out EOS_ProductUserId peerId, socketIdPtr, out byte channel, dataBuffer, out uint bytesWritten);

            if (recvResult == EOS_EResult.Success && bytesWritten > 0)
            {
                byte[] managedData = new byte[bytesWritten];
                Marshal.Copy(dataBuffer, managedData, 0, (int)bytesWritten);

                string peerIdStr = ProductUserIdToString(peerId);
                _log?.Invoke($"[EOS] Received packet from {peerIdStr} channel={channel} {bytesWritten} bytes");
                OnPacketReceived?.Invoke(peerIdStr, managedData, channel);
            }
            else if (recvResult != EOS_EResult.Success)
            {
                _log?.Invoke($"[EOS] ReceivePacket FAILED: {recvResult}");
            }

            Marshal.FreeHGlobal(dataBuffer);
            Marshal.FreeHGlobal(socketIdPtr);

            if (recvResult != EOS_EResult.Success)
                break;
        }
    }

    /// <summary>
    /// Get the list of currently connected peers.
    /// </summary>
    public List<EOS_ProductUserId> GetConnectedPeers()
    {
        lock (_peerLock)
        {
            return new List<EOS_ProductUserId>(_connectedPeers);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  ProductUserId Serialization
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Convert an EOS_ProductUserId to a portable string (e.g. "0002a5b8c9d0e1f2").
    /// Uses EOS_ProductUserId_ToString internally — the result is safe to transmit across processes.
    /// </summary>
    public static string ProductUserIdToString(EOS_ProductUserId userId)
    {
        if (userId.Handle == IntPtr.Zero || EosNativeLib.EOS_ProductUserId_IsValid(userId) == 0)
            return "";

        int bufferLen = 64;
        IntPtr buffer = Marshal.AllocHGlobal(bufferLen);
        try
        {
            EosNativeLib.EOS_ProductUserId_ToString(userId, buffer, ref bufferLen);
            return Marshal.PtrToStringUTF8(buffer) ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Convert a portable string back to an EOS_ProductUserId.
    /// Uses EOS_ProductUserId_FromString internally.
    /// </summary>
    public static EOS_ProductUserId ProductUserIdFromString(string str)
    {
        if (string.IsNullOrEmpty(str))
            return new EOS_ProductUserId(IntPtr.Zero);

        IntPtr ptr = Marshal.StringToCoTaskMemUTF8(str);
        try
        {
            return EosNativeLib.EOS_ProductUserId_FromString(ptr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Cleanup
    // ═══════════════════════════════════════════════════════════════

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Unregister notifications
        if (_initialized && _p2p.Handle.IsValid())
        {
            if (_connectionRequestNotificationId.IsValid)
                EosNativeLib.EOS_P2P_RemoveNotifyPeerConnectionRequest(_p2p, _connectionRequestNotificationId);
            if (_connectionEstablishedNotificationId.IsValid)
                EosNativeLib.EOS_P2P_RemoveNotifyPeerConnectionEstablished(_p2p, _connectionEstablishedNotificationId);
        }

        // Release platform
        if (_initialized && _platform.Handle.IsValid())
        {
            EosNativeLib.EOS_Platform_Release(_platform);
        }

        // Shutdown SDK
        if (_initialized)
        {
            EosNativeLib.EOS_Shutdown();
        }

        _log?.Invoke("[EOS] Shutdown complete");
        _initialized = false;
    }
}