using System;
using BlueSky.Core.Scripting;

namespace BlueSky.Networking;

/// <summary>
/// Bridges the TeaScript runtime API (login, host, search, join, loadScene) to the
/// real <see cref="IMultiplayerService"/> and <see cref="SessionManager"/> implementations.
///
/// Call <see cref="Register"/> once after creating your multiplayer service —
/// in <c>Standalone</c> mode the real EOS service is passed in; in the
/// in-editor play mode you can pass <see langword="null"/> and networking reports an offline state.
/// </summary>
public static class NetworkingTeaScriptBridge
{
    // ─── Live service reference ──────────────────────────────────────────────

    /// <summary>The currently active multiplayer service (null = offline / in-editor).</summary>
    public static IMultiplayerService? ActiveService { get; private set; }

    /// <summary>The session manager for hosting/searching/joining.</summary>
    public static SessionManager? Session { get; private set; }

    /// <summary>The lobby manager for player list and server travel.</summary>
    public static EosLobbyManager? Lobby { get; private set; }

    /// <summary>
    /// Callback invoked when a TeaScript calls <c>loadScene(path, mode, sessionName, joinId)</c>.
    /// The four arguments are: scenePath, mode ("offline"|"host"|"join"),
    /// sessionName, joinSessionId.
    /// </summary>
    public static Action<string, string, string, string>? OnLoadSceneRequested { get; private set; }

    // ─── Auth state exposed to scripts ───────────────────────────────────────

    private static string _authStatus = "LoggedOut";
    private static string _displayName = "";
    private static bool _loginInProgress;

    /// <summary>Current auth status string visible to TeaScript via getAuthStatus().</summary>
    public static string AuthStatus
    {
        get => _authStatus;
        private set => _authStatus = value;
    }

    /// <summary>Current player display name visible to TeaScript via getDisplayName().</summary>
    public static string DisplayName
    {
        get => _displayName;
        private set => _displayName = value;
    }

    // ─── Registration ────────────────────────────────────────────────────────

    /// <summary>
    /// Register the live service and an optional scene-load handler.
    /// Safe to call multiple times; replaces the previous registration.
    /// </summary>
    public static void Register(IMultiplayerService? service,
                                Action<string, string, string, string>? onLoadScene = null)
    {
        ActiveService = service;
        OnLoadSceneRequested = onLoadScene;
        Session = new SessionManager(service);
        Lobby = new EosLobbyManager(Session);
        RefreshAuthStatus();
    }

    // ─── Helpers called by TeaScriptSystem ───────────────────────────────────

    /// <summary>
    /// Trigger an EOS login. Invokes <paramref name="onResult"/> with
    /// (success, message) once the browser redirect completes (or fails).
    /// </summary>
    public static void Login(string playerName, Action<bool, string> onResult)
    {
        if (ActiveService == null)
        {
            Console.WriteLine("[Net Bridge] No multiplayer service — login ignored (editor mode).");
            onResult(false, "No multiplayer service available in editor play mode.");
            return;
        }

        if (_loginInProgress)
        {
            Console.WriteLine("[Net Bridge] Login already in progress; ignoring duplicate request.");
            return;
        }

        _loginInProgress = true;
        _authStatus = "Authenticating...";

        ActiveService.Login(playerName, (success, msg) =>
        {
            _loginInProgress = false;
            RefreshAuthStatus();
            onResult(success, msg);
        });
    }

    /// <summary>
    /// Load a scene, routing through <see cref="OnLoadSceneRequested"/> if set,
    /// or printing a console message if not.
    /// </summary>
    public static void LoadScene(string scenePath, string mode, string sessionName, string joinSessionId)
    {
        if (OnLoadSceneRequested != null)
        {
            OnLoadSceneRequested(scenePath, mode, sessionName, joinSessionId);
        }
        else
        {
            Console.WriteLine($"[Net Bridge] loadScene({scenePath}, {mode}) — no handler registered.");
        }
    }

    /// <summary>Synchronises <see cref="AuthStatus"/> and <see cref="DisplayName"/> with the current service state.</summary>
    public static void RefreshAuthStatus()
    {
        if (ActiveService == null)
        {
            _authStatus = "Offline";
            _displayName = "";
            return;
        }

        if (!ActiveService.IsInitialized)
        {
            _authStatus = "Not Initialised";
            _displayName = "";
            return;
        }

        if (ActiveService.IsLoggedIn)
        {
            string realName = GetEosDisplayName();
            if (string.IsNullOrEmpty(realName))
                realName = ActiveService.Config?.LocalPlayerName ?? "Player";

            _displayName = realName;
            _authStatus = $"LoggedIn ({realName})";
        }
        else
        {
            _authStatus = "LoggedOut";
            _displayName = "";
        }
    }

    /// <summary>
    /// Tick the active multiplayer service to pump EOS SDK callbacks.
    /// Must be called every frame when a service is registered.
    /// Without this, async operations like AccountPortal login will never complete.
    /// </summary>
    public static void Tick(double deltaTime)
    {
        if (ActiveService is EosMultiplayerService eosService)
        {
            eosService.Tick(deltaTime);
        }
        Lobby?.Update((float)deltaTime);
    }

    // ─── Host / Search / Join ────────────────────────────────────────────────

    /// <summary>
    /// Host a new session. Called from TeaScript via host(sessionName, scenePath, maxPlayers).
    /// </summary>
    public static void Host(string sessionName, string scenePath, int maxPlayers, Action<string, string> onResult)
    {
        if (Session == null) { onResult("error", "SessionManager not initialized"); return; }

        var session = Session.Host(sessionName, DisplayName, scenePath, maxPlayers);
        if (session != null)
        {
            onResult("ok", session.SessionId);
        }
        else
        {
            onResult("error", "Failed to host");
        }
    }

    /// <summary>
    /// Search for available sessions. Called from TeaScript via search().
    /// </summary>
    public static void Search()
    {
        if (Session == null) return;
        Session.Search();
    }

    /// <summary>
    /// Join a session by index in the discovered list. Called from TeaScript via join(index).
    /// </summary>
    public static bool Join(int index)
    {
        if (Session == null) return false;
        return Session.JoinByIndex(index);
    }

    /// <summary>Get the number of discovered sessions.</summary>
    public static int GetSessionCount() => Session?.GetSessionCount() ?? 0;

    /// <summary>Get session name at a specific index.</summary>
    public static string GetSessionName(int index) => Session?.GetSessionName(index) ?? "";

    /// <summary>Get session host at a specific index.</summary>
    public static string GetSessionHost(int index) => Session?.GetSessionHost(index) ?? "";

    /// <summary>Get player count of a session at a specific index.</summary>
    public static int GetSessionPlayerCount(int index) => Session?.GetSessionPlayerCount(index) ?? 0;

    /// <summary>Get max players of a session at a specific index.</summary>
    public static int GetSessionMaxPlayers(int index) => Session?.GetSessionMaxPlayers(index) ?? 0;

    // ── Lobby helpers ────────────────────────────────────────────

    /// <summary>Host a lobby and enter the lobby screen.</summary>
    public static bool HostLobby(string lobbyName, string scenePath, int maxPlayers, string hostName, string hostUserId)
    {
        return Lobby?.HostLobby(lobbyName, scenePath, maxPlayers, hostName, hostUserId) ?? false;
    }

    /// <summary>Join an existing lobby.</summary>
    public static bool JoinLobby(string lobbyName, string scenePath, string playerName, string userId)
    {
        return Lobby?.JoinLobby(lobbyName, scenePath, playerName, userId) ?? false;
    }

    /// <summary>Start the countdown to server travel (host only).</summary>
    public static void StartCountdown(float seconds) => Lobby?.StartCountdown(seconds);

    /// <summary>Trigger server travel immediately.</summary>
    public static void TriggerTravel() => Lobby?.TriggerTravel();

    /// <summary>Update lobby countdown. Call every frame.</summary>
    public static void UpdateLobby(float deltaTime) => Lobby?.Update(deltaTime);

    /// <summary>Leave the current lobby.</summary>
    public static void LeaveLobby() => Lobby?.LeaveLobby();

    /// <summary>Get lobby player count.</summary>
    public static int GetLobbyPlayerCount() => Lobby?.GetPlayerCount() ?? 0;

    /// <summary>Get lobby player name at index.</summary>
    public static string GetLobbyPlayerName(int index) => Lobby?.GetPlayerName(index) ?? "";

    /// <summary>Get lobby countdown seconds remaining.</summary>
    public static float GetLobbyCountdown() => Lobby?.CountdownSeconds ?? 0;

    /// <summary>Get lobby countdown progress (0-1).</summary>
    public static float GetLobbyCountdownProgress() => Lobby?.GetCountdownProgress() ?? 0;

    /// <summary>Is in a lobby?</summary>
    public static bool IsInLobby() => Lobby?.IsInLobby ?? false;

    /// <summary>Is the host?</summary>
    public static bool IsLobbyHost() => Lobby?.IsHost ?? false;

    /// <summary>Add a player to the lobby (for host when a peer joins).</summary>
    public static void AddLobbyPlayer(string userId, string displayName) => Lobby?.AddPlayer(userId, displayName);

    /// <summary>Remove a player from the lobby.</summary>
    public static void RemoveLobbyPlayer(string userId) => Lobby?.RemovePlayer(userId);
    private static string GetEosDisplayName()
    {
        if (ActiveService is EosMultiplayerService eosService)
        {
            return eosService.EosDisplayName;
        }
        return "";
    }
}
