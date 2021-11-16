using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using UnityEngine.UI;

public class NetworkedServer : MonoBehaviour
{
    int maxConnections = 1000;
    int reliableChannelID;
    int unreliableChannelID;
    int hostID;
    int socketPort = 10563;

    LinkedList<PlayerAccount> playerAccounts;
    LinkedList<string> listOfActions;

    const int PlayerAccountNameAndPassword = 1;

    const string IndexFilePath = "PlayerAccounts.txt";
    string playerAccountFilePath;

    // replay system
    const string ReplayFilePath = "ReplayList.txt";
    string replayListFilePath;

    int playerWaitingForMatchWithID = -1;

    LinkedList<GameRoom> gameRooms;

    // tic tac toe UI, for any users joining in part way, keep a record of the board on the server to communicate to the spectator client
    //
    // [0,0] [1,0] [2,0]
    // [0,1] [1,1] [2,1]
    // [0,2] [1,2] [2,2]
    //
    private int[,] ticTacToeServerBoard;

  

    // Start is called before the first frame update
    void Start()
    {
        NetworkTransport.Init();
        ConnectionConfig config = new ConnectionConfig();
        reliableChannelID = config.AddChannel(QosType.Reliable);
        unreliableChannelID = config.AddChannel(QosType.Unreliable);
        HostTopology topology = new HostTopology(config, maxConnections);
        hostID = NetworkTransport.AddHost(topology, socketPort, null);

        // Create a localized server copy of the board
        ticTacToeServerBoard = new int[3, 3];
        ResetServerBoard();


        playerAccountFilePath = Application.dataPath + Path.DirectorySeparatorChar + IndexFilePath;
        replayListFilePath = Application.dataPath + Path.DirectorySeparatorChar + ReplayFilePath;
        
        playerAccounts = new LinkedList<PlayerAccount>();
        gameRooms = new LinkedList<GameRoom>();

        // REPLAY FUNCTIONALITY, save items on a list of strings
        listOfActions = new LinkedList<string>();
        // READ THE LIST
        LoadPlayerAccounts();

    }

    // Update is called once per frame
    void Update()
    {

        int recHostID;
        int recConnectionID;
        int recChannelID;
        byte[] recBuffer = new byte[1024];
        int bufferSize = 1024;
        int dataSize;
        byte error = 0;

        NetworkEventType recNetworkEvent = NetworkTransport.Receive(out recHostID, out recConnectionID, out recChannelID, recBuffer, bufferSize, out dataSize, out error);

        switch (recNetworkEvent)
        {
            case NetworkEventType.Nothing:
                break;
            case NetworkEventType.ConnectEvent:
                Debug.Log("Connection, " + recConnectionID);
                break;
            case NetworkEventType.DataEvent:
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                ProcessReceivedMsg(msg, recConnectionID);
                break;
            case NetworkEventType.DisconnectEvent:
                Debug.Log("Disconnection, " + recConnectionID);
                // should find a way to disconnect the game room when we cut off
                
                break;
        }

    }

    /// <summary>
    /// Sends a message to the specific id
    /// </summary>
    /// <param name="msg"></param>
    /// <param name="id"></param>
    public void SendMessageToClient(string msg, int id)
    {
        byte error = 0;
        byte[] buffer = Encoding.Unicode.GetBytes(msg);
        NetworkTransport.Send(hostID, id, reliableChannelID, buffer, msg.Length * sizeof(char), out error);

        if (error != 0)
        {
            Debug.Log("nooooooooooo");
        }
    }

    private void ProcessReceivedMsg(string msg, int id)
    {
        Debug.Log("msg recieved = " + msg + ".  connection id = " + id);

        string[] csv = msg.Split(',');

        // when the message has been received, get the signifier
        int signifier = int.Parse(csv[0]);

        // SIGNIFIER 1
        if (signifier == ClientToServerSignifiers.CreateAccount)
        {
            Debug.Log("Create Account");
            // check if player account already exists,
            // if not, create new account, add to list
            // and save this to the HD
            // send to client success/failure
            string n = csv[1];
            string p = csv[2];
            bool nameInUse = false;

            // run through every existing account
            foreach (PlayerAccount pa in playerAccounts)
            {
                if (pa.name == n)
                {
                    nameInUse = true;
                    break;
                }
            }

            if (nameInUse)
            {
                SendMessageToClient(ServerToClientSignifiers.AccountCreationFailed + "", id); // using the id that was sent in the process message
                                                                                              // send back to the client that account creation ahs failed
            }
            else
            {
                // creating a new player account
                PlayerAccount newPlayerAccount = new PlayerAccount(n, p);

                playerAccounts.AddLast(newPlayerAccount);
                SendMessageToClient(ServerToClientSignifiers.AccountCreationComplete + "", id);

                //save list to hard drive ** TO DO ***
                SavePlayerAccounts();
            }
        }
        // SIGNIFIER 2
        else if (signifier == ClientToServerSignifiers.Login)
        {
            // log in to existing acccount
            // check if player account name already exists
            // check if password matches
            // send to client success or failure
            string n = csv[1];
            string p = csv[2];

            bool isNameFound = false;
            bool msgHasBeenSentToClient = false;

            foreach (PlayerAccount pa in playerAccounts)
            {
                if (n == pa.name)
                {
                    isNameFound = true;
                    if (p == pa.password)
                    {
                        Debug.Log("Login Account");
                        SendMessageToClient(ServerToClientSignifiers.LoginComplete + "", id);
                        msgHasBeenSentToClient = true;
                    }
                    else
                    {
                        Debug.Log("Login Failed");

                        SendMessageToClient(ServerToClientSignifiers.LoginFailed + "", id);
                        msgHasBeenSentToClient = true;
                    }
                }
            }

            if (!isNameFound)
            {
                if (!msgHasBeenSentToClient)
                {
                    SendMessageToClient(ServerToClientSignifiers.LoginFailed + "", id);
                }
            }


        }
        // SIGNIFIER 3
        else if (signifier == ClientToServerSignifiers.WaitingToJoinGameRoom)
        {
            // first time that a client sends a message, store the id of the client with this variable
            if (playerWaitingForMatchWithID == -1)
            {
                Debug.Log("We need to get this player into a waiting queue");
                playerWaitingForMatchWithID = id; // id becomes 1 in the first shot
            }

            else // second time you run through
            {

                // but what if the player has left the game room?

                // add game room to a list of game rooms
                // player waiting for match with ID = 1, id = 2
                // assign ID
                // hard coded sadly
                if (id <= 2)
                {
                    GameRoom gr = new GameRoom(playerWaitingForMatchWithID, id);
                    gameRooms.AddLast(gr);

                // send message to both clients
                // TODO: upon game start, assign the proper ID to the player... just pass in gr.playerNum
                // TODO: change so that we can scale up to as many players as we can as spectatorss

                
                    SendMessageToClient(ServerToClientSignifiers.GameStart + "," + gr.players[0], gr.players[0]);
                    SendMessageToClient(ServerToClientSignifiers.GameStart + "," + gr.players[1], gr.players[1]);

                    // establish turn order
                    SendMessageToClient(ServerToClientSignifiers.ChangeTurn + "," + gr.players[0], gr.players[0]);
                    SendMessageToClient(ServerToClientSignifiers.ChangeTurn + "," + gr.players[0], gr.players[1]);

                    Debug.Log("Room Established");
                }
                // assuming that the connection ID that's greater than 2 will automatically be considered a spectator
                if (id > 2)
                {
                    // TO-DO: Add spectators when the player count exceeds 2
                    GameRoom gr = GetGameRoomWithClientID(1);
                    gr.AddSpectator(id);
                    SendMessageToClient(ServerToClientSignifiers.MidwayJoin + "," + id, id);

                    // run through the whole board and fill in data from the server to the client
                    for (int i = 0; i < ticTacToeServerBoard.GetLength(0); i++)
                    {
                        for (int j = 0; j < ticTacToeServerBoard.GetLength(1); j++)
                        {
                            SendMessageToClient(ServerToClientSignifiers.UpdateSpectator + "," + i + "," + j + "," + ticTacToeServerBoard[i, j], id);
                        }
                    }

                    // on the local board, update all the board units
                }
                else
                {
                    //playerWaitingForMatchWithID = -1; // meaning the player isn't waiting anymore... would we still need this for spectators
                }
            }
        }

        // once we have a game room set up ... not actually sending this signifier in
        // SIGNIFIER 4
        else if (signifier == ClientToServerSignifiers.TicTacToe)
        {
            GameRoom gr = GetGameRoomWithClientID(id);
            if (gr != null)
            {
                if (gr.players[0] == id)
                {
                    SendMessageToClient(ServerToClientSignifiers.OpponentPlay + "", gr.players[0]); // might not need this
                }
                else
                {
                    SendMessageToClient(ServerToClientSignifiers.OpponentPlay + "", gr.players[1]);
                }
            }
        }

        // SIGNIFIER 5
        else if (signifier == ClientToServerSignifiers.PlayerAction)
        {
            GameRoom gr = GetGameRoomWithClientID(id);
            if (gr != null)
            {
                int currentTurn;
                // if it was player 1 that sent this in, send over to player 2
                // intended order should be signifier, row, column, playerID (if player ID is 1, send it to 2 and vice versa)
                Debug.Log(ServerToClientSignifiers.OpponentPlay + "," + csv[1] + "," + csv[2] + "," + csv[3]);

                // add to the local server board
                ticTacToeServerBoard[int.Parse(csv[1]), int.Parse(csv[2])] = int.Parse(csv[3]);

                if (gr.players[0] == id)
                {
                    currentTurn = gr.players[1];
                    SendMessageToClient(ServerToClientSignifiers.OpponentPlay + "," + csv[1] + "," + csv[2] + "," + csv[3], gr.players[1]);
                }
                else
                {
                    currentTurn = gr.players[0];
                    SendMessageToClient(ServerToClientSignifiers.OpponentPlay + "," + csv[1] + "," + csv[2] + "," + csv[3], gr.players[0]);
                }

                // update turn order
                SendMessageToClient(ServerToClientSignifiers.ChangeTurn + "," + currentTurn, gr.players[0]);
                SendMessageToClient(ServerToClientSignifiers.ChangeTurn + "," + currentTurn, gr.players[1]);

                foreach (int spectators in gr.spectators)
                {
                    SendMessageToClient(ServerToClientSignifiers.UpdateSpectator + "," + csv[1] + "," + csv[2] + "," + csv[3], spectators);
                }
            }
        }
        // SIGNIFIER 6
        else if (signifier == ClientToServerSignifiers.SendPresetMessage)
        {
            Debug.Log("Process Message: " + ClientToServerSignifiers.SendPresetMessage + "," + csv[1]);
            GameRoom gr = GetGameRoomWithClientID(id);
            
            if (gr != null)
            {
                if (gr.players[0] == id)
                {
                    SendMessageToClient(ServerToClientSignifiers.SendMessage + "," + csv[1], gr.players[1]);
                }
                else
                {
                    SendMessageToClient(ServerToClientSignifiers.SendMessage + "," + csv[1], gr.players[0]);
                }

            }
        }
        // SIGNIFIER 7
        else if (signifier == ClientToServerSignifiers.PlayerWins)
        {
            GameRoom gr = GetGameRoomWithClientID(id);
            if (gr != null)
            {

                if (gr.players[0] == id)
                {
                    SendMessageToClient(ServerToClientSignifiers.NotifyOpponentWin + "," + id, gr.players[1]);
                }
                else
                {
                    SendMessageToClient(ServerToClientSignifiers.NotifyOpponentWin + "," + id , gr.players[0]);
                }
            }
        }
        // SIGNIFIER 8
        else if (signifier == ClientToServerSignifiers.ResetGame)
        {
            GameRoom gr = GetGameRoomWithClientID(id);
            if (gr != null)
            {
                ResetServerBoard();
                if (gr.players[0] == id)
                {
                    SendMessageToClient(ServerToClientSignifiers.GameReset + "", gr.players[1]);
                }
                else
                {
                    SendMessageToClient(ServerToClientSignifiers.GameReset + "", gr.players[0]);
                }

                foreach (int spectator in gr.spectators)
                {
                    SendMessageToClient(ServerToClientSignifiers.ResetSpectator + "", spectator);
                }
            }
        }

    }

    private void ResetServerBoard()
    {
	    for (int i = 0; i<ticTacToeServerBoard.GetLength(0); i++)
	    {
	       for (int j = 0; j<ticTacToeServerBoard.GetLength(1); i++)
	       {
	           ticTacToeServerBoard[i, j] = 0;
	       }
	    }
    }

    private void SavePlayerAccounts()
    {
        StreamWriter sw = new StreamWriter(playerAccountFilePath);
        foreach (PlayerAccount pa in playerAccounts)
        {
            sw.WriteLine(PlayerAccountNameAndPassword + "," + pa.name + "," + pa.password);
        }
        sw.Close();
    }

    private void LoadPlayerAccounts()
    {

        if (File.Exists(playerAccountFilePath))
        {
            StreamReader sr = new StreamReader(playerAccountFilePath); // index file path should be called PlayerAccounts.txt

            string line;
            while ((line = sr.ReadLine()) != null)
            {
                string[] csv = line.Split(',');
                int signifier = int.Parse(csv[0]);
                if (signifier == PlayerAccountNameAndPassword)
                {
                    PlayerAccount tempPlayerAccount = new PlayerAccount(csv[1], csv[2]);
                    playerAccounts.AddLast(tempPlayerAccount);
                }
            }
            sr.Close();
        }
    }

    private GameRoom GetGameRoomWithClientID(int id)
    {
        foreach(GameRoom gr in gameRooms)
        {
            if (gr.players[0] == id || gr.players[1] == id)
            {
                return gr;
            } 
            // check spectator ID
            else
            {
                foreach (int spectator in gr.spectators)
                {
                    if (spectator == id)
                    {
                        return gr;
                    }
                }
            }
        }
        return null;
    }
}

public class PlayerAccount
{
    public string name, password;
    public PlayerAccount(string inName, string inPassword)
    {
        name = inName;
        password = inPassword;
    }
}

public class GameRoom
{
    public List<int> players;
    public List<int> spectators;
    public GameRoom(int playerID1, int playerID2)
    {
        players = new List<int>();
        spectators = new List<int>();
        players.Add(playerID1);
        players.Add(playerID2);

        // only need to worry about these two
        Debug.Log("Players " + players[0] + "," + players[1]);

    }

    public GameRoom()
    {
        players = new List<int>();
        
    }

    // adding a spectator to the game room... could be redundant if spectators is already public
    public void AddSpectator(int playerID)
    {
        spectators.Add(playerID);
    }
}


public static class ClientToServerSignifiers
{
    public const int CreateAccount = 1;
    public const int Login = 2;
    public const int WaitingToJoinGameRoom = 3;
    public const int TicTacToe = 4;
    public const int PlayerAction = 5;
    public const int SendPresetMessage = 6;
    public const int PlayerWins = 7;
    public const int ResetGame = 8;
    public const int LogAction = 9;
    public const int RequestReplay = 10;

}
public static class ServerToClientSignifiers
{
    public const int LoginComplete = 1;
    public const int LoginFailed = 2;
    public const int AccountCreationComplete = 3;
    public const int AccountCreationFailed = 4;
    public const int OpponentPlay = 5; // once player makes an action, send an action back to the receiver client
    public const int GameStart = 6;
    public const int SendMessage = 7;
    public const int NotifyOpponentWin = 8; // notify to the opponent that there's a win
    public const int ChangeTurn = 9;
    public const int GameReset = 10;
    public const int MidwayJoin = 11;
    public const int UpdateSpectator = 12;
    public const int ResetSpectator = 13;
}