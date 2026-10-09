using System;
using System.Runtime.InteropServices;

namespace BlueSky.Networking.EOS;

internal static class EOSHandleExtensions
{
    public static bool IsValid(this IntPtr handle) => handle != IntPtr.Zero;
}

// ── Opaque handle types ─────────────────────────────────────────────────
[StructLayout(LayoutKind.Sequential)]
public readonly struct EOS_HPlatform { public readonly IntPtr Handle; }
[StructLayout(LayoutKind.Sequential)]
public readonly struct EOS_HAuth { public readonly IntPtr Handle; }
[StructLayout(LayoutKind.Sequential)]
public readonly struct EOS_HConnect { public readonly IntPtr Handle; }
[StructLayout(LayoutKind.Sequential)]
public readonly struct EOS_HP2P { public readonly IntPtr Handle; }
[StructLayout(LayoutKind.Sequential)]
public readonly struct EOS_HUserInfo { public readonly IntPtr Handle; }
[StructLayout(LayoutKind.Sequential)]
public readonly struct EOS_EpicAccountId { public readonly IntPtr Handle; }
[StructLayout(LayoutKind.Sequential)]
public readonly struct EOS_ProductUserId
{
    public readonly IntPtr Handle;
    public EOS_ProductUserId(IntPtr handle) { Handle = handle; }
}

public readonly struct EOS_NotificationId
{
    public readonly uint Id;
    public static readonly EOS_NotificationId Invalid = default;
    public bool IsValid => Id != 0;
    public EOS_NotificationId(uint id) { Id = id; }
}

// ── Enums ───────────────────────────────────────────────────────────────
public enum EOS_EResult : int
{
    Success = 0, NoConnection = 1, InvalidCredentials = 2, InvalidUser = 3,
    InvalidAuth = 4, AccessDenied = 5, MissingPermissions = 6, TooManyRequests = 8,
    AlreadyPending = 9, InvalidParameters = 10, InvalidRequest = 11,
    UnrecognizedResponse = 12, IncompatibleVersion = 13, NotConfigured = 14,
    AlreadyConfigured = 15, NotImplemented = 16, Canceled = 17, NotFound = 18,
    OperationWillRetry = 19, NoChange = 20, VersionMismatch = 21, UnexpectedError = 30,
}

public enum EOS_ELoginCredentialType : int
{
    Password = 0, ExchangeCode = 1, PersistentAuth = 2, DeviceCode = 3,
    Developer = 4, RefreshToken = 5, AccountPortal = 6, ExternalAuth = 7,
}

public enum EOS_ELoginStatus : int { NotLoggedIn = 0, LoggingIn = 1, LoggedIn = 2 }

public enum EOS_EExternalCredentialType : int
{
    Epic = 0, Steam = 1, Discord = 2, Xbox = 3, PlayStation = 4, Nintendo = 5, Uplay = 6,
    OpenID = 7, EpicIdToken = 8, Amazon = 9, Apple = 10, Google = 11, Oculus = 12,
    Itchio = 13, EpicRefreshToken = 14, DeviceCode = 15,
}

public enum EOS_EPacketReliability : int { UnreliableUnordered = 0, ReliableUnordered = 1, ReliableOrdered = 2 }
public enum EOS_EAuthTokenType : int { Client = 0, User = 1 }

public enum EOS_ELogLevel : int
{
    Off = 0,
    Fatal = 100,
    Error = 200,
    Warning = 300,
    Info = 400,
    Verbose = 500,
    VeryVerbose = 600
}

public enum EOS_ELogCategory : int
{
    AllCategories = 0
}

[StructLayout(LayoutKind.Sequential)]
public struct EOS_LogMessage
{
    public IntPtr Category;
    public IntPtr Message;
    public EOS_ELogLevel Level;
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void EOS_LogMessageFunc(ref EOS_LogMessage message);

// ── Platform structs ────────────────────────────────────────────────────
[StructLayout(LayoutKind.Sequential)]
public struct EOS_InitializeOptions
{
    public int ApiVersion;
    public IntPtr AllocateMemoryFunction;
    public IntPtr ReallocateMemoryFunction;
    public IntPtr ReleaseMemoryFunction;
    public IntPtr ProductName;
    public IntPtr ProductVersion;
    public IntPtr Reserved;
    public IntPtr SystemInitializeOptions;
    public IntPtr OverrideThreadAffinity;
}

[StructLayout(LayoutKind.Sequential)]
public struct EOS_Platform_ClientCredentials
{
    public IntPtr ClientId;
    public IntPtr ClientSecret;
}

[StructLayout(LayoutKind.Sequential)]
public struct EOS_Platform_Options
{
    public int ApiVersion;
    public IntPtr Reserved;
    public IntPtr ProductId;
    public IntPtr SandboxId;
    public EOS_Platform_ClientCredentials ClientCredentials;
    public int bIsServer;
    public IntPtr EncryptionKey;
    public IntPtr OverrideCountryCode;
    public IntPtr OverrideLocaleCode;
    public IntPtr DeploymentId;
    public long Flags;
    public IntPtr CacheDirectory;
    public uint TickBudgetInMilliseconds;
    public IntPtr RTCOptions;
    public IntPtr IntegratedPlatformOptionsContainerHandle;
    public IntPtr SystemSpecificOptions;
    public IntPtr TaskNetworkTimeoutSeconds;
}

// ── Auth structs ────────────────────────────────────────────────────────
[Flags]
public enum EOS_EAuthScopeFlags : int
{
    NoFlags = 0x0,
    BasicProfile = 0x1,
    FriendsList = 0x2,
    Presence = 0x4,
    FriendsManagement = 0x8,
    Email = 0x10,
    Country = 0x20
}

[StructLayout(LayoutKind.Sequential)]
public struct EOS_Auth_Credentials
{
    public int ApiVersion;
    public IntPtr Id;
    public IntPtr Token;
    public EOS_ELoginCredentialType Type;
    public IntPtr SystemAuthCredentialsOptions;
    public EOS_EExternalCredentialType ExternalType;
}

[StructLayout(LayoutKind.Sequential)]
public struct EOS_Auth_LoginOptions
{
    public int ApiVersion;
    public IntPtr Credentials;
    public EOS_EAuthScopeFlags ScopeFlags;
    public ulong LoginFlags;
}

[StructLayout(LayoutKind.Sequential)]
public struct EOS_Auth_LoginCallbackInfo
{
    public EOS_EResult ResultCode;
    public IntPtr ClientData;
    public EOS_EpicAccountId LocalUserId;
    public IntPtr PinGrantInfo;
    public IntPtr ContinuanceToken;
    public IntPtr AccountFeatureRestrictedInfo_DEPRECATED;
    public EOS_EpicAccountId SelectedAccountId;
}

[StructLayout(LayoutKind.Sequential)]
public struct EOS_Auth_Token
{
    public int ApiVersion;
    public IntPtr App;
    public IntPtr ClientId;
    public EOS_EpicAccountId AccountId;
    public IntPtr AccessToken;
    public double ExpiresIn;
    public IntPtr ExpiresAt;
    public EOS_EAuthTokenType AuthType;
    public IntPtr RefreshToken;
    public double RefreshExpiresIn;
    public IntPtr RefreshExpiresAt;
}

[StructLayout(LayoutKind.Sequential)]
public struct EOS_Auth_CopyUserAuthTokenOptions
{
    public int ApiVersion;
}

[StructLayout(LayoutKind.Sequential)]
public struct EOS_Auth_LogoutCallbackInfo
{
    public EOS_EResult ResultCode;
    public IntPtr ClientData;
    public EOS_EpicAccountId LocalUserId;
}

// ── Connect structs ─────────────────────────────────────────────────────
[StructLayout(LayoutKind.Sequential)]
public struct EOS_Connect_Credentials
{
    public int ApiVersion;
    public IntPtr Token;
    public EOS_EExternalCredentialType Type;
}

[StructLayout(LayoutKind.Sequential)]
public struct EOS_Connect_LoginOptions
{
    public int ApiVersion;
    public IntPtr Credentials;
    public IntPtr UserLoginInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct EOS_Connect_LoginCallbackInfo
{
    public EOS_EResult ResultCode;
    public IntPtr ClientData;
    public EOS_ProductUserId LocalUserId;
    public IntPtr ContinuanceToken;
}

[StructLayout(LayoutKind.Sequential)]
public struct EOS_Connect_CreateUserOptions
{
    public int ApiVersion;
    public IntPtr ContinuanceToken;
}

[StructLayout(LayoutKind.Sequential)]
public struct EOS_Connect_CreateUserCallbackInfo
{
    public EOS_EResult ResultCode;
    public IntPtr ClientData;
    public EOS_ProductUserId LocalUserId;
}

// ── P2P structs ─────────────────────────────────────────────────────────
[StructLayout(LayoutKind.Sequential)]
public struct EOS_P2P_SocketId
{
    public int ApiVersion;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
    public string SocketName;
}

[StructLayout(LayoutKind.Sequential)]
public struct EOS_P2P_SendPacketOptions
{
    public int ApiVersion;
    public EOS_ProductUserId LocalUserId;
    public EOS_ProductUserId RemoteUserId;
    public IntPtr SocketId;
    public byte Channel;
    public uint DataLengthBytes;
    public IntPtr Data;
    public int bAllowDelayedDelivery;
    public EOS_EPacketReliability Reliability;
    public int bDisableAutoAcceptConnection;
}

[StructLayout(LayoutKind.Sequential)]
public struct EOS_P2P_GetNextReceivedPacketSizeOptions
{
    public int ApiVersion;
    public EOS_ProductUserId LocalUserId;
    public IntPtr RequestedChannel;
}

[StructLayout(LayoutKind.Sequential)]
public struct EOS_P2P_ReceivePacketOptions
{
    public int ApiVersion;
    public EOS_ProductUserId LocalUserId;
    public uint MaxDataSizeBytes;
    public IntPtr RequestedChannel;
}

[StructLayout(LayoutKind.Sequential)]
public struct EOS_P2P_AcceptConnectionOptions
{
    public int ApiVersion;
    public EOS_ProductUserId LocalUserId;
    public EOS_ProductUserId RemoteUserId;
    public IntPtr SocketId;
}

// EOS_P2P_OpenConnectionOptions removed — no such API in the EOS SDK

[StructLayout(LayoutKind.Sequential)]
public struct EOS_P2P_CloseConnectionOptions
{
    public int ApiVersion;
    public EOS_ProductUserId LocalUserId;
    public EOS_ProductUserId RemoteUserId;
    public IntPtr SocketId;
}

[StructLayout(LayoutKind.Sequential)]
public struct EOS_P2P_CloseConnectionsOptions
{
    public int ApiVersion;
    public EOS_ProductUserId LocalUserId;
    public IntPtr SocketId;
}

[StructLayout(LayoutKind.Sequential)]
public struct EOS_P2P_AddNotifyPeerConnectionRequestOptions
{
    public int ApiVersion;
    public IntPtr LocalUserId;
    public IntPtr SocketId;
}

[StructLayout(LayoutKind.Sequential)]
public struct EOS_P2P_OnIncomingConnectionRequestInfo
{
    public IntPtr ClientData;
    public EOS_ProductUserId LocalUserId;
    public EOS_ProductUserId RemoteUserId;
    public IntPtr SocketId;
}

[StructLayout(LayoutKind.Sequential)]
public struct EOS_P2P_OnPeerConnectionEstablishedInfo
{
    public IntPtr ClientData;
    public EOS_ProductUserId LocalUserId;
    public EOS_ProductUserId RemoteUserId;
    public IntPtr SocketId;
    public int ConnectionType;
    public int NetworkType;
}

// ── UserInfo structs ────────────────────────────────────────────────────
[StructLayout(LayoutKind.Sequential)]
public struct EOS_UserInfo_QueryUserInfoOptions
{
    public int ApiVersion;
    public EOS_EpicAccountId LocalUserId;
    public EOS_EpicAccountId TargetUserId;
}

[StructLayout(LayoutKind.Sequential)]
public struct EOS_UserInfo_QueryUserInfoCallbackInfo
{
    public EOS_EResult ResultCode;
    public IntPtr ClientData;
    public EOS_EpicAccountId LocalUserId;
    public EOS_EpicAccountId TargetUserId;
}

[StructLayout(LayoutKind.Sequential)]
public struct EOS_UserInfo_CopyUserInfoOptions
{
    public int ApiVersion;
    public EOS_EpicAccountId LocalUserId;
    public EOS_EpicAccountId TargetUserId;
}

[StructLayout(LayoutKind.Sequential)]
public struct EOS_UserInfo
{
    public int ApiVersion;
    public EOS_EpicAccountId UserId;
    public IntPtr Country;
    public IntPtr DisplayName;
    public IntPtr PreferredLanguage;
    public IntPtr Nickname;
    public IntPtr DisplayNameSanitized;
}

// ── Callbacks ───────────────────────────────────────────────────────────
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void EOS_Auth_OnLoginCallback(ref EOS_Auth_LoginCallbackInfo data);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void EOS_Auth_OnLogoutCallback(ref EOS_Auth_LogoutCallbackInfo data);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void EOS_Connect_OnLoginCallback(ref EOS_Connect_LoginCallbackInfo data);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void EOS_Connect_OnCreateUserCallback(ref EOS_Connect_CreateUserCallbackInfo data);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void EOS_P2P_OnIncomingConnectionRequestCallback(ref EOS_P2P_OnIncomingConnectionRequestInfo data);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void EOS_P2P_OnPeerConnectionEstablishedCallback(ref EOS_P2P_OnPeerConnectionEstablishedInfo data);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void EOS_UserInfo_OnQueryUserInfoCallback(ref EOS_UserInfo_QueryUserInfoCallbackInfo data);

// ── Constants (matching EOS SDK v1.19.1.2) ─────────────────────────────
public static class EOS_Constants
{
    public const int EOS_INITIALIZE_API_LATEST = 5;
    public const int EOS_PLATFORM_OPTIONS_API_LATEST = 15;
    public const int EOS_AUTH_LOGIN_API_LATEST = 3;
    public const int EOS_AUTH_CREDENTIALS_API_LATEST = 4;
    public const int EOS_AUTH_TOKEN_API_LATEST = 2;
    public const int EOS_AUTH_COPYAUTHTOKEN_API_LATEST = 1;
    public const int EOS_CONNECT_LOGIN_API_LATEST = 2;
    public const int EOS_CONNECT_CREDENTIALS_API_LATEST = 1;
    public const int EOS_CONNECT_USERLOGININFO_API_LATEST = 2;
    public const int EOS_CONNECT_CREATEUSER_API_LATEST = 1;
    public const int EOS_P2P_SENDPACKET_API_LATEST = 3;
    public const int EOS_P2P_GETNEXTRECEIVEDPACKETSIZE_API_LATEST = 2;
    public const int EOS_P2P_RECEIVEPACKET_API_LATEST = 1;
    public const int EOS_P2P_ACCEPTCONNECTION_API_LATEST = 1;
    public const int EOS_P2P_SOCKETID_API_LATEST = 1;
    public const int EOS_P2P_ADDNOTIFYPEERCONNECTIONREQUEST_API_LATEST = 1;
    public const int EOS_USERINFO_QUERYUSERINFO_API_LATEST = 1;
    public const int EOS_USERINFO_COPYUSERINFO_API_LATEST = 3;

    public const int EOS_PF_LOADING_IN_EDITOR = 0x00001;
    public const int EOS_PF_DISABLE_OVERLAY = 0x00002;
    public const int EOS_PF_DISABLE_SOCIAL_OVERLAY = 0x00004;

    public const int EOS_TRUE = 1;
    public const int EOS_FALSE = 0;
    public const uint EOS_P2P_MAX_PACKET_SIZE = 1170;
}

// ── DllImport Bindings ──────────────────────────────────────────────────
internal static class EosNativeLib
{
    private const string DllName = "libEOSSDK-Mac-Shipping";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern EOS_EResult EOS_Initialize(ref EOS_InitializeOptions options);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern EOS_EResult EOS_Shutdown();
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern EOS_HPlatform EOS_Platform_Create(ref EOS_Platform_Options options);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void EOS_Platform_Release(EOS_HPlatform handle);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void EOS_Platform_Tick(EOS_HPlatform handle);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern EOS_HAuth EOS_Platform_GetAuthInterface(EOS_HPlatform handle);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern EOS_HConnect EOS_Platform_GetConnectInterface(EOS_HPlatform handle);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern EOS_HP2P EOS_Platform_GetP2PInterface(EOS_HPlatform handle);

    // Auth
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void EOS_Auth_Login(EOS_HAuth handle, IntPtr options, IntPtr clientData, EOS_Auth_OnLoginCallback cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void EOS_Auth_Logout(EOS_HAuth handle, IntPtr options, IntPtr clientData, EOS_Auth_OnLogoutCallback cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int EOS_Auth_GetLoggedInAccountsCount(EOS_HAuth handle);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern EOS_EpicAccountId EOS_Auth_GetLoggedInAccountByIndex(EOS_HAuth handle, int index);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern EOS_ELoginStatus EOS_Auth_GetLoginStatus(EOS_HAuth handle, EOS_EpicAccountId localUserId);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern EOS_EResult EOS_Auth_CopyUserAuthToken(EOS_HAuth handle, IntPtr options, EOS_EpicAccountId localUserId, out IntPtr outUserAuthToken);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void EOS_Auth_Token_Release(IntPtr authToken);

    // Connect
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void EOS_Connect_Login(EOS_HConnect handle, IntPtr options, IntPtr clientData, EOS_Connect_OnLoginCallback cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void EOS_Connect_CreateUser(EOS_HConnect handle, IntPtr options, IntPtr clientData, EOS_Connect_OnCreateUserCallback cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern EOS_EResult EOS_Connect_GetLoggedInUsersCount(EOS_HConnect handle);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern EOS_ProductUserId EOS_Connect_GetLoggedInUserByIndex(EOS_HConnect handle, int index);

    // P2P
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern EOS_EResult EOS_P2P_SendPacket(EOS_HP2P handle, ref EOS_P2P_SendPacketOptions options);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern EOS_EResult EOS_P2P_GetNextReceivedPacketSize(EOS_HP2P handle, ref EOS_P2P_GetNextReceivedPacketSizeOptions options, out uint outPacketSizeBytes);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern EOS_EResult EOS_P2P_ReceivePacket(EOS_HP2P handle, ref EOS_P2P_ReceivePacketOptions options, out EOS_ProductUserId outPeerId, IntPtr outSocketId, out byte outChannel, IntPtr outData, out uint outBytesWritten);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern EOS_EResult EOS_P2P_AcceptConnection(EOS_HP2P handle, ref EOS_P2P_AcceptConnectionOptions options);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern EOS_EResult EOS_P2P_CloseConnection(EOS_HP2P handle, ref EOS_P2P_CloseConnectionOptions options);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern EOS_EResult EOS_P2P_CloseConnections(EOS_HP2P handle, ref EOS_P2P_CloseConnectionsOptions options);
    // EOS_P2P_OpenConnection does not exist in the EOS SDK.
    // Connections are established implicitly by sending packets.
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern EOS_NotificationId EOS_P2P_AddNotifyPeerConnectionRequest(EOS_HP2P handle, ref EOS_P2P_AddNotifyPeerConnectionRequestOptions options, IntPtr clientData, EOS_P2P_OnIncomingConnectionRequestCallback handler);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void EOS_P2P_RemoveNotifyPeerConnectionRequest(EOS_HP2P handle, EOS_NotificationId notificationId);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern EOS_NotificationId EOS_P2P_AddNotifyPeerConnectionEstablished(EOS_HP2P handle, IntPtr options, IntPtr clientData, EOS_P2P_OnPeerConnectionEstablishedCallback cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void EOS_P2P_RemoveNotifyPeerConnectionEstablished(EOS_HP2P handle, EOS_NotificationId notificationId);

    // Helpers
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr EOS_EResult_ToString(EOS_EResult result);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int EOS_EResult_IsOperationComplete(EOS_EResult result);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int EOS_EpicAccountId_IsValid(EOS_EpicAccountId accountId);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int EOS_ProductUserId_IsValid(EOS_ProductUserId productUserId);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int EOS_ProductUserId_ToString(EOS_ProductUserId userId, IntPtr outBuffer, ref int inOutBufferLength);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern EOS_ProductUserId EOS_ProductUserId_FromString(IntPtr accountIdString);

    // UserInfo
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern EOS_HUserInfo EOS_Platform_GetUserInfoInterface(EOS_HPlatform handle);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void EOS_UserInfo_QueryUserInfo(EOS_HUserInfo handle, IntPtr options, IntPtr clientData, EOS_UserInfo_OnQueryUserInfoCallback cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern EOS_EResult EOS_UserInfo_CopyUserInfo(EOS_HUserInfo handle, IntPtr options, out IntPtr outUserInfo);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void EOS_UserInfo_Release(IntPtr userInfo);

    // Logging
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern EOS_EResult EOS_Logging_SetCallback(EOS_LogMessageFunc callback);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern EOS_EResult EOS_Logging_SetLogLevel(EOS_ELogCategory logCategory, EOS_ELogLevel logLevel);
}
