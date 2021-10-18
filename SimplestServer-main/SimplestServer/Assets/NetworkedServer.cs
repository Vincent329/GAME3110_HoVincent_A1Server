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
    int socketPort = 5491;

    LinkedList<PlayerAccount> playerAccounts;

    const int PlayerAccountNameAndPassword = 1;

    const string IndexFilePath = "PlayerAccounts.txt";
    string playerAccountFilePath;

    int playerWaitingForMatchWithID = -1;

    LinkedList<GameRoom> gameRooms;

    // Start is called before the first frame update
    void Start()
    {
        NetworkTransport.Init();
        ConnectionConfig config = new ConnectionConfig();
        reliableChannelID = config.AddChannel(QosType.Reliable);
        unreliableChannelID = config.AddChannel(QosType.Unreliable);
        HostTopology topology = new HostTopology(config, maxConnections);
        hostID = NetworkTransport.AddHost(topology, socketPort, null);


        playerAccountFilePath = Application.dataPath + Path.DirectorySeparatorChar + IndexFilePath;
        playerAccounts = new LinkedList<PlayerAccount>();
        gameRooms = new LinkedList<GameRoom>();
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
                ProcessRecievedMsg(msg, recConnectionID);
                break;
            case NetworkEventType.DisconnectEvent:
                Debug.Log("Disconnection, " + recConnectionID);
                break;
        }

    }

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

    private void ProcessRecievedMsg(string msg, int id)
    {
        Debug.Log("msg recieved = " + msg + ".  connection id = " + id);

        string[] csv = msg.Split(',');

        int signifier = int.Parse(csv[0]);

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
        else if (signifier == ClientToServerSignifiers.Login)
        {
            Debug.Log("Login Account");
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
                        SendMessageToClient(ServerToClientSignifiers.LoginComplete + "", id);
                        msgHasBeenSentToClient = true;
                    }
                    else
                    {
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
        else if (signifier == ClientToServerSignifiers.WaitingToJoinQueue)
        {
            Debug.Log("We need to get this player into a waiting queue");
            // first time that a client sends a message, store the id of the client with this variable
            if (playerWaitingForMatchWithID == -1)
            {
                playerWaitingForMatchWithID = id;
            } 
                    
            else // second time you run through
            {
                // but what if the player has left the game room?

                // add game room to a list of game rooms
                GameRoom gr = new GameRoom(playerWaitingForMatchWithID, id);
                gameRooms.AddLast(gr);

                // send message to both clients
                SendMessageToClient(ServerToClientSignifiers.GameStart + "", gr.player1);
                SendMessageToClient(ServerToClientSignifiers.GameStart + "", gr.player2);


                playerWaitingForMatchWithID = -1; // meaning the player isn't waiting anymore
            }
        } 
        else if (signifier == ClientToServerSignifiers.TicTacToe)
        {
            GameRoom gr = GetGameRoomWithClientID(id);
            if (gr != null)
            {
                if (gr.player1 == id)
                {
                    SendMessageToClient(ServerToClientSignifiers.OpponentPlay + "", gr.player1);
                } else
                {
                    SendMessageToClient(ServerToClientSignifiers.OpponentPlay + "", gr.player2);
                }
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
            if (gr.player1 == id || gr.player2 == id)
            {
                return gr;
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
    public int player1, player2;
    public GameRoom(int playerID1, int playerID2)
    {
        player1 = playerID1;
        player2 = playerID2;

    }
}

public static class ClientToServerSignifiers
{
    public const int CreateAccount = 1;
    public const int Login = 2;
    public const int WaitingToJoinQueue = 3;
    public const int TicTacToe = 4;
}
public static class ServerToClientSignifiers
{
    public const int LoginComplete = 1;
    public const int LoginFailed = 2;
    public const int AccountCreationComplete = 3;
    public const int AccountCreationFailed = 4;
    public const int OpponentPlay = 5;
    public const int GameStart = 6;
}