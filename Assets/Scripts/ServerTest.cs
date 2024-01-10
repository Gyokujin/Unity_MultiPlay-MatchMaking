using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FishNet.Managing;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Matchmaker;
using Unity.Services.Matchmaker.Models;
using UnityEngine;
using FishNet.Connection;
using FishNet.Transporting;
using Unity.Services.Core.Environments;
using Unity.Services.Multiplay;

public class ServerTest : MonoBehaviour
{
    private bool _isServer = false;
    private string _externalServerIP = "0.0.0.0";
    private ushort _serverPort = 7770;

    void Start()
    {
        var args = System.Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-dedicatedServer":
                    _isServer = true;
                    break;
                case "-ip":
                    if (args.Length > i + 1)
                    {
                        _externalServerIP = args[i + 1];
                    }
                    break;
                case "-port":
                    if (args.Length > i + 1)
                    {
                        _serverPort = ushort.Parse(args[i + 1]);
                    }
                    break;
            }
        }

        if (_isServer)
        {
            Server.StartServer(_externalServerIP, _serverPort);
        }
        else
        {
            Client.StartClient().Forget();
        }
    }

    private void Update()
    {
        if (_isServer)
        {
            Server.UpdateServer();
        }
    }
}

public class Server
{
    private const string NA = "n/a";
    public const int MAX_PLAYERS = 100;
    private static IServerQueryHandler _serverQueryHandler;

    private static bool AllowEnter
    {
        get
        {
            // Debug.Log($"Client Count: {NetworkManager.Instance.ElementAt(0).ServerManager.Clients.Count}, Max Players: {MAX_PLAYERS}");

            return NetworkManager.Instances.ElementAt(0).ServerManager.Clients.Count < MAX_PLAYERS;
        }
    }

    private static string _serverIP = "0.0.0.0";
    private static ushort _serverPort = 7770;


    public static async void StartServer(string externalServerIP, ushort serverPort)
    {
        Debug.Log(nameof(StartServer) + $"({externalServerIP}:{serverPort})");
        _serverIP = externalServerIP;
        _serverPort = serverPort;


        Debug.Log($"Init Server");
        Camera.main.enabled = false;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 30;


        Debug.Log($"Start Server Services");
        var option = new InitializationOptions();
        option.SetEnvironmentName("production");
        await UnityServices.InitializeAsync(option);


        Debug.Log($"Get MatchMaker Paylord");
        var serverCallbacks = new MultiplayEventCallbacks();
        serverCallbacks.Allocate -= OnMultiplayerAllocation;
        serverCallbacks.Allocate += OnMultiplayerAllocation;
        serverCallbacks.Deallocate -= OnMultiplayerDeallocation;
        serverCallbacks.Deallocate += OnMultiplayerDeallocation;
        serverCallbacks.Error -= OnMultiplayerError;
        serverCallbacks.Error += OnMultiplayerError;
        serverCallbacks.SubscriptionStateChanged -= OnMultiplayerSubscriptionStateChanged;
        serverCallbacks.SubscriptionStateChanged += OnMultiplayerSubscriptionStateChanged;


        Debug.Log($"Subscribe To Server Events");
        var multiplayService = MultiplayService.Instance;
        await multiplayService.SubscribeToServerEventsAsync(serverCallbacks);


        Debug.Log($"Start Server Query Handler");
        _serverQueryHandler = await multiplayService.StartServerQueryHandlerAsync(MAX_PLAYERS, "TestServer" + Tool.GetRandomString(3), NA, NA, NA);


        Debug.Log("Ready Server For Players");
        await multiplayService.ReadyServerForPlayersAsync();
    }

    public static void UpdateServer()
    {
        if (_serverQueryHandler == null) { return; }

        _serverQueryHandler.CurrentPlayers = (ushort)NetworkManager.Instances.ElementAt(0).ServerManager.Clients.Count;
        _serverQueryHandler.UpdateServerCheck();
    }


    private static async void OnMultiplayerAllocation(MultiplayAllocation allocation)
    {
        Debug.Log(nameof(OnMultiplayerAllocation));

        Debug.Log($"Start Server Connection");
        Debug.Log($"Server Port: {_serverPort}");

        var networkManager = NetworkManager.Instances.ElementAt(0);
        networkManager.ServerManager.StartConnection(_serverPort);
        networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
        networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;

        var multiplayService = MultiplayService.Instance;
        var serverConfigs = multiplayService.ServerConfig;
        Debug.Log($"Server ID[{serverConfigs.ServerId}]");
        Debug.Log($"Server Allocation ID[{serverConfigs.AllocationId}]");
        Debug.Log($"Server Port[{serverConfigs.Port}]");
        Debug.Log($"Server Query Port[{serverConfigs.QueryPort}]");
        Debug.Log($"Server Log Directory[{serverConfigs.ServerLogDirectory}]");


        Debug.Log($"Get Payload Allocation");
        var matchmakingResult = await MultiplayService.Instance.GetPayloadAllocationFromJsonAs<MatchmakingResults>();


        TryLoopingApproveBackfilling(networkManager, matchmakingResult.BackfillTicketId).Forget();
    }

    private static void OnMultiplayerDeallocation(MultiplayDeallocation deallocation)
    {
        Debug.Log(nameof(OnMultiplayerDeallocation));
    }

    private static void OnMultiplayerError(MultiplayError error)
    {
        Debug.Log(nameof(OnMultiplayerError));
    }

    private static void OnMultiplayerSubscriptionStateChanged(MultiplayServerSubscriptionState state)
    {
        Debug.Log(nameof(OnMultiplayerSubscriptionStateChanged));
    }


    private static async UniTask TryLoopingApproveBackfilling(NetworkManager networkManager, string backfillTicketID)
    {
        Debug.Log(nameof(TryLoopingApproveBackfilling) + $"backfillTicketID: {backfillTicketID}");

        while (AllowEnter)
        {
            await UniTask.Delay(1000);
            // Debug.Log($"BackfillTicket ID: {backfillTicketID});

            var localBackfillTicket = await MatchmakerService.Instance.ApproveBackfillTicketAsync(backfillTicketID); // 중간참여를 의미한다. 난입
            // Debug.Log($"Local BackfillTicket ID: {localBackfillTicket.ID}");
            backfillTicketID = localBackfillTicket.Id;
        }
    }


    private static void OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
    {
        Debug.Log(nameof(OnRemoteConnectionState) + $" Stage: {args.ConnectionState}, Connection Id: {connection.ClientId}");

        var networkManager = NetworkManager.Instances.ElementAt(0);

        switch (args.ConnectionState)
        {
            case RemoteConnectionState.Stopped:
                if (networkManager.ServerManager.Clients.Count == 0)
                {
                    Debug.Log("Server Stop by all clients disconnecting");
                    Application.Quit();
                    break;
                }

                // if (_doBackFilling == false && AllowEnter)
                // {
                //     TryLoopingApproveBackfilling(networkManager, _backkfillTicketID).Forget();
                // }
                break;
        }
    }
}

public class Client
{
    private static string PlayerID
    {
        get
        {
            return AuthenticationService.Instance.PlayerId;
        }
    }

    private static string _ticketId;
    private static MultiplayAssignment _multiplayAssignment;

    public static async UniTask StartClient()
    {
        Debug.Log(nameof(StartClient));

        await InitializeAndSignIn();

        await CreateTicket();
    }

    private static async UniTask InitializeAndSignIn()
    {
        Debug.Log(nameof(InitializeAndSignIn));

        var serverProfileName = $"User{Tool.GetRandomString(10)}";

        var initOptions = new InitializationOptions();
        initOptions.SetProfile(serverProfileName);
        await UnityServices.InitializeAsync(initOptions);


        await AuthenticationService.Instance.SignUpWithUsernamePasswordAsync($"{serverProfileName}", "Password1!");

        Debug.Log($"Signed in as {serverProfileName}");
    }

    private static async UniTask CreateTicket()
    {
        Debug.Log(nameof(CreateTicket));

        var options = new CreateTicketOptions("NetworkTestQueue01");
        // var options = new CreateTicketOptions();

        var players = new List<Player>
        {
            new Player(
                PlayerID,
                new MatchData
                {
                    EnterCode = "Enter"
                }
            )
        };

        var ticketResponse = await MatchmakerService.Instance.CreateTicketAsync(players, options);
        _ticketId = ticketResponse.Id;

        await PoolTicketStatus();
    }

    private static async UniTask PoolTicketStatus()
    {
        Debug.Log(nameof(PoolTicketStatus));

        _multiplayAssignment = null;
        var gotAssignment = false;

        do
        {
            await Task.Delay(1000);
            var ticketStatus = await MatchmakerService.Instance.GetTicketAsync(_ticketId);
            if (ticketStatus == null) { continue; }
            if (ticketStatus.Type == typeof(MultiplayAssignment))
            {
                _multiplayAssignment = ticketStatus.Value as MultiplayAssignment;
            }

            Debug.Log($"Ticket Status: {_multiplayAssignment.Status}, Message: {_multiplayAssignment.Message}");

            switch (_multiplayAssignment.Status)
            {
                case MultiplayAssignment.StatusOptions.Found:
                    gotAssignment = true;
                    StartClientConnection(_multiplayAssignment);
                    break;
                case MultiplayAssignment.StatusOptions.InProgress:
                    break;
                case MultiplayAssignment.StatusOptions.Failed:
                    gotAssignment = true;
                    Debug.Log("Failed to get assignment");
                    break;
                case MultiplayAssignment.StatusOptions.Timeout:
                    gotAssignment = true;
                    Debug.Log("Timed out");
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }
        while (gotAssignment == false);
    }

    private static void StartClientConnection(MultiplayAssignment assignment)
    {
        Debug.Log(nameof(StartClientConnection));

        Debug.Log($"Ticket assigned: {assignment.Ip}:{assignment.Port}");

        var networkManager = NetworkManager.Instances.ElementAt(0);
        networkManager.ClientManager.StartConnection(assignment.Ip, (ushort)assignment.Port);
    }
}

[System.Serializable]
public class MatchData
{
    public string EnterCode;
}

public class Tool
{
    public static string GetRandomString(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return new string(Enumerable.Repeat(chars, length)
        .Select(s => s[UnityEngine.Random.Range(0, s.Length)]).ToArray());
    }
}