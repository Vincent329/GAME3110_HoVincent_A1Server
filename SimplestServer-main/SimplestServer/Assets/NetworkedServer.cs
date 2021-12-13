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
    int socketPort = 10653;

    LinkedList<PlayerAccount> playerAccounts;

    const int PlayerAccountNameAndPassword = 1;

    const string IndexFilePath = "PlayerAccounts.txt";
    string playerAccountFilePath;


    int playerWaitingForMatchWithID = -1;

    LinkedList<GameRoom> gameRooms;
  
    // tic tac toe UI, for any users joining in part way, keep a record of the board on the server to communicate to the spectator client
    //
    // [0,0] [1,0] [2,0]
    // [0,1] [1,1] [2,1]
    // [0,2] [1,2] [2,2]
    //
    private int[,] ticTacToeServerBoard;



    // ---------------- replay system ----------------------------

    // Replay System
    int lastIndexUsed;
    LinkedList<string> listOfActions;

    //List<string> replayNames; // list of replay names
    LinkedList<NameAndIndex> replayNameAndIndices;

    const string ReplayFilePath = "ReplayList.txt";
    string replayListFilePath;
       
    const int LastUsedIndexSignifier = 1;
    const int IndexAndNameSignifier = 2;

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
        
        playerAccounts = new LinkedList<PlayerAccount>();
        gameRooms = new LinkedList<GameRoom>();

        // REPLAY FUNCTIONALITY, save items on a list of strings
        listOfActions = new LinkedList<string>();
        replayListFilePath = Application.dataPath + Path.DirectorySeparatorChar + ReplayFilePath;

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
                SendMessageToClient(ServerToClientSignifiers.AccountCreationComplete + "," + n, id);

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
                        SendMessageToClient(ServerToClientSignifiers.LoginComplete + "," + n, id);
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
                if (id <= 2)
                {
                    Debug.Log("We need to get this player into a waiting queue");
                    playerWaitingForMatchWithID = id; // id becomes 1 in the first shot
                }
                else if (id > 2)
                {
                    // TO-DO: Add spectators when the player count exceeds 2
                    GameRoom gr = GetGameRoomWithClientID(1);
                    if (gr == null)
                    {
                        gr = GetGameRoomWithClientID(2);
                    }
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
                }
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
                    // TODO: change so that we can scale up to as many players as we can as spectators

                    SendMessageToClient(ServerToClientSignifiers.GameStart + "," + gr.players[0], gr.players[0]);
                    SendMessageToClient(ServerToClientSignifiers.GameStart + "," + gr.players[1], gr.players[1]);

                    // establish turn order
                    SendMessageToClient(ServerToClientSignifiers.ChangeTurn + "," + gr.players[0], gr.players[0]);
                    SendMessageToClient(ServerToClientSignifiers.ChangeTurn + "," + gr.players[0], gr.players[1]);

                    // initialize the list with current replay data at hand
                    InitializeReplayList();

                    // be able to update the list of replays for players when they enter
                    // TEST: Send over information line by line
                    // for every list in replay name and indices list, update the list over on the client side
                    foreach (NameAndIndex nameAndIndex in replayNameAndIndices)
                    {
                        SendMessageToClient(ServerToClientSignifiers.UpdateReplayList + "," + nameAndIndex.index + "," + nameAndIndex.replayName, gr.players[0]);
                        SendMessageToClient(ServerToClientSignifiers.UpdateReplayList + "," + nameAndIndex.index + "," + nameAndIndex.replayName, gr.players[1]);
                    }


                    playerWaitingForMatchWithID = -1; // meaning the player isn't waiting anymore... would we still need this for spectators

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

                // add to the list of actions... row, column, playerIDAtPosition
                listOfActions.AddLast(csv[1] + "," + csv[2] + "," + csv[3]);

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
                foreach (int player in gr.players)
                {
                    SendMessageToClient(ServerToClientSignifiers.SendMessage + "," + csv[1], player);
                }
                foreach (int spectator in gr.spectators)
                {
                    SendMessageToClient(ServerToClientSignifiers.SendMessage + "," + csv[1], spectator);
                }
            }
        }
        // SIGNIFIER 7
        else if (signifier == ClientToServerSignifiers.PlayerWins)
        {
            GameRoom gr = GetGameRoomWithClientID(id);
            if (gr != null)
            {
                // TO DO: save all actions to a string file (IMPORTANT)
                // debug, output all the actions taken
                foreach (string actions in listOfActions)
                {
                    Debug.Log(actions);
                    // log all these actions into a text file
                }

                if (gr.players[0] == id)
                {
                    SendMessageToClient(ServerToClientSignifiers.NotifyOpponentWin + "," + id, gr.players[1]);
                }
                else
                {
                    SendMessageToClient(ServerToClientSignifiers.NotifyOpponentWin + "," + id, gr.players[0]);
                }

                // IMPORTANT TEST: Save the replay locally to the client
                // register only on victory
                SaveReplay();
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
                    Debug.Log("Reset for P2");
                    SendMessageToClient(ServerToClientSignifiers.GameReset + "", gr.players[1]);
                }
                else
                {
                    Debug.Log("Reset for P1");
                    SendMessageToClient(ServerToClientSignifiers.GameReset + "", gr.players[0]);
                }

                // UPDATE cleared board for any spectators
                foreach (int spectator in gr.spectators)
                {
                    SendMessageToClient(ServerToClientSignifiers.ResetSpectator + "", spectator);
                }

                listOfActions.Clear();

            }
        }
        else if (signifier == ClientToServerSignifiers.SaveReplay)
        {
            GameRoom gr = GetGameRoomWithClientID(id);
            if (gr != null)
            {
                SendMessageToClient(ServerToClientSignifiers.UpdateReplayList + "," + replayNameAndIndices.Last.Value.index + "," + replayNameAndIndices.Last.Value.replayName, gr.players[0]);
                SendMessageToClient(ServerToClientSignifiers.UpdateReplayList + "," + replayNameAndIndices.Last.Value.index + "," + replayNameAndIndices.Last.Value.replayName, gr.players[1]);
                SendMessageToClient(ServerToClientSignifiers.ReplaySaved + "", gr.players[0]);
                SendMessageToClient(ServerToClientSignifiers.ReplaySaved + "", gr.players[1]);
            }
        }
        else if (signifier == ClientToServerSignifiers.RequestReplay)
        {
            Debug.Log("ReplayRequested @:" + csv[1]);
            // send message to client through here
            LoadReplays(csv[1]);
        }

    }

    private void ResetServerBoard()
    {
	    for (int i = 0; i < ticTacToeServerBoard.GetLength(0); i++)
	    {
	       for (int j = 0; j < ticTacToeServerBoard.GetLength(1); j++)
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

    // ----------------- REPLAY SYSTEM FUNCTIONS ---------------------------
    private void InitializeReplayList()
    {
        replayNameAndIndices = new LinkedList<NameAndIndex>();

        if (File.Exists(replayListFilePath))
        {
            Debug.Log("Found File");
            StreamReader sr = new StreamReader(replayListFilePath);

            string line;
            while ((line = sr.ReadLine()) != null)
            {
                //Debug.Log(line);
                string[] csv = line.Split(',');
                int signifier = int.Parse(csv[0]);

                // searches the indices list, if it's the last used index signifier, it'll store the value of the last item on the list
                if (signifier == LastUsedIndexSignifier)
                {
                    lastIndexUsed = int.Parse(csv[1]);
                }
                // otherwise we detail the list at the start of runtime
                else if (signifier == IndexAndNameSignifier)
                {
                    replayNameAndIndices.AddLast(new NameAndIndex(int.Parse(csv[1]), csv[2]));
                }
            }
        }

        //RefreshReplayNameList();
        
        // find some way to send the updated list over to the client side of players 1 and 2
        // SendMessageToClient(ServerToClientSignifier.UpdateIndex + "", players);
    }

    /// <summary>
    /// update an index of replays to the server
    /// </summary>
    private void SaveReplay()
    {
        lastIndexUsed++;
        
        // With this function call, save list of actions to the text file
        SaveReplayToList(Application.dataPath + Path.DirectorySeparatorChar + lastIndexUsed + ".txt");
        replayNameAndIndices.AddLast(new NameAndIndex(lastIndexUsed, "Replay " + lastIndexUsed));

        SaveIndexManagementFile();
        //RefreshReplayNameList();
        Debug.Log("Saving...");
    }

    private void SaveIndexManagementFile()
    {
        StreamWriter sw = new StreamWriter(Application.dataPath + Path.DirectorySeparatorChar + ReplayFilePath);
        sw.WriteLine(LastUsedIndexSignifier + "," + lastIndexUsed);

        foreach(NameAndIndex nameAndIndex in replayNameAndIndices)
        {
            sw.WriteLine(IndexAndNameSignifier + "," + nameAndIndex.index + "," + nameAndIndex.replayName);
        }
        sw.Close();
    }

    private void SaveReplayToList(string fileName)
    {
        StreamWriter sr = new StreamWriter(fileName);

        foreach(string line in listOfActions)
        {
            sr.WriteLine(line);
        }
        sr.Close();
    }

    private void LoadReplays(string selectedName)
    {
        int indexToLoad = -1;

        foreach (NameAndIndex nameAndIndex in replayNameAndIndices)
        {
            // find the name in the list
            if (nameAndIndex.replayName == selectedName)
            {
                indexToLoad = nameAndIndex.index;
            }
        }
        Debug.Log("Load " + selectedName);
        PlayReplay(indexToLoad);
    }

    /// <summary>
    /// Plays the replay
    /// </summary>
    /// <param name="replayIndex"></param>
    private void PlayReplay(int replayIndex)
    {
        StreamReader sr = new StreamReader(Application.dataPath + Path.DirectorySeparatorChar + replayIndex + ".txt");
        GameRoom gr = GetGameRoomWithClientID(1);
        if (gr == null)
        {
            gr = GetGameRoomWithClientID(2);
        }
        string line;

        foreach (int player in gr.players)
        {
            SendMessageToClient(ServerToClientSignifiers.StartReplay + "", player);
        }
     
        while ((line = sr.ReadLine()) != null)
        {
            string[] csv = line.Split(',');
            ticTacToeServerBoard[int.Parse(csv[0]), int.Parse(csv[1])] = int.Parse(csv[2]);

            foreach (int player in gr.players)
            {
                SendMessageToClient(ServerToClientSignifiers.ProcessReplay + "," + csv[0] + "," + csv[1] + "," + csv[2], player);
            }
            foreach (int spectator in gr.spectators)
            {
                SendMessageToClient(ServerToClientSignifiers.ProcessReplay + "," + csv[0] + "," + csv[1] + "," + csv[2], spectator);
            }
        }
        foreach (int player in gr.players)
        {
            SendMessageToClient(ServerToClientSignifiers.EndReplay + "", player);
        }

    }

    // this is the structure of how we will be saving the list of replays
    public class NameAndIndex
    {
        public string replayName;
        public int index;

        public NameAndIndex(int inIndex, string inName)
        {
            replayName = inName;
            index = inIndex;
        }
    }



    // ----------------- Game Room functionalities ----------------------

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

    // Game process actions
    public const int PlayerAction = 5;
    public const int SendPresetMessage = 6;
    public const int PlayerWins = 7;
    public const int ResetGame = 8;

    // Replay System functionality
    public const int SaveReplay = 9;
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

    // spectator functions
    public const int MidwayJoin = 11;
    public const int UpdateSpectator = 12;
    public const int ResetSpectator = 13;

    // replay functionality
    public const int ProcessReplay = 14;
    public const int UpdateReplayList = 15;
    public const int StartReplay = 16;
    public const int EndReplay = 17;
    public const int ReplaySaved = 18;

}