﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;
using Classes;

namespace Server
{
    public partial class Server : Form
    {
        public Server()
        {
            InitializeComponent();

            clientList = new List<ClientInfo>();
            findingList = new List<ClientInfo>();
            games = new List<Game>();
        }

        //The collection of all clients logged into the room (an array of type ClientInfo)
        private List<ClientInfo> clientList;

        public string getClientName(Socket clientSocket)
        {
            return (from ClientInfo client in clientList where client.Socket == clientSocket select client.User.name).FirstOrDefault();
        }

        private List<ClientInfo> findingList;
        private List<Game> games;

        //The main socket on which the server listens to the clients
        private Socket serverSocket;

        private byte[] byteData = new byte[1024];

        private void Form1_Load(object sender, EventArgs e)
        {
            try
            {
                //We are using TCP sockets
                serverSocket = new Socket(AddressFamily.InterNetwork,
                                          SocketType.Stream,
                                          ProtocolType.Tcp);

                //Assign the any IP of the machine and listen on port number 8000
                IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Any, 8000);

                //Bind and listen on the given address
                serverSocket.Bind(ipEndPoint);
                serverSocket.Listen(100);

                //Accept the incoming clients
                serverSocket.BeginAccept(OnAccept, serverSocket);
                txtLog.Text += string.Format("[{0}] The server is listening on port 8000\r\n", DateTime.Now.ToString("hh:mm:ss"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Server: Form1_Load",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnAccept(IAsyncResult ar)
        {
            try
            {
                Socket clientSocket = serverSocket.EndAccept(ar);

                //Start listening for more clients
                serverSocket.BeginAccept(OnAccept, serverSocket);

                //Once the client connects then start receiving the commands from her
                clientSocket.BeginReceive(byteData, 0, byteData.Length, SocketFlags.None,
                    OnReceive, clientSocket);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Server: OnAccept",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnReceive(IAsyncResult ar)
        {
            try
            {
                Socket clientSocket = (Socket)ar.AsyncState;
                clientSocket.EndReceive(ar);

                //Transform the array of bytes received from the User into an
                //intelligent form of object Data
                Data msgReceived = new Data(byteData);

                //We will send this object in response the users request
                Data msgToSend = new Data();

                byte[] message;

                //If the message is to login, logout, or simple text message
                //then when send to others the type of the message remains the same
                msgToSend.Command = msgReceived.Command;
                msgToSend.Name = msgReceived.Name;

                int nIndex;
                switch (msgReceived.Command)
                {
                    case Command.Login:
                        var user = UserDAO.Login(msgReceived.Name, msgReceived.Password);
                        if (user != null)
                        {
                            //When a User logs in to the server then we add her to our
                            //list of clients

                            var clientInfo = new ClientInfo { Socket = clientSocket, User = user };

                            clientList.Add(clientInfo);

                            txtLog.Text += String.Format("[{0}] {1} has logged in\r\n",
                                DateTime.Now.ToString("hh:mm:ss"),
                                msgReceived.Name
                                );

                            msgToSend.Command = Command.User;
                            message = msgToSend.ToByte(user);

                            //Send the name of the users in the chat room
                            clientSocket.BeginSend(message, 0, message.Length, SocketFlags.None,
                                    OnSend, clientSocket);

                            clientSocket.BeginReceive(byteData, 0, byteData.Length, SocketFlags.None, OnReceive, clientSocket);
                        }
                        else
                        {
                            msgToSend.Command = Command.Error;
                            msgToSend.Message = "Invalid name/password, please try again";

                            message = msgToSend.ToByte();

                            //Send the name of the users in the chat room
                            //IAsyncResult result = clientSocket.BeginSend(message, 0, message.Length, SocketFlags.None, new AsyncCallback(OnSend), clientSocket);
                            clientSocket.Send(message, 0, message.Length, SocketFlags.None);
                            //while (!result.IsCompleted)
                            //{
                            //    // Do more work here if the call isn't complete
                            //    Thread.Sleep(100);
                            //}

                            nIndex = 0;
                            foreach (ClientInfo client in clientList)
                            {
                                if (client.Socket == clientSocket)
                                {
                                    clientList.RemoveAt(nIndex);
                                    break;
                                }
                                ++nIndex;
                            }
                            clientSocket.Close();
                        }
                        break;

                    case Command.Logout:

                        //When a User wants to log out of the server then we search for her 
                        //in the list of clients and close the corresponding connection
                        clientList.RemoveAll(x => x.Socket == clientSocket);
                        findingList.RemoveAll(x => x.Socket == clientSocket);
                        games.RemoveAll(x => x.player1.Socket == clientSocket || x.player2.Socket == clientSocket);
                        // TODO: Notificate opponent about leave game by User
                        // TODO: Recalc rank of the users

                        clientSocket.Close();

                        msgToSend.Message = "<<<" + msgReceived.Name + " has disconnected from server>>>";
                        txtLog.Text += string.Format("[{0}] {1} has disconnected from server\r\n", DateTime.Now.ToString("hh:mm:ss"), msgReceived.Name);
                        break;

                    case Command.Message:
                        txtLog.Text += msgReceived.Name + ": " + msgReceived.Message + "\r\n";
                        //Set the text of the message that we will broadcast to all users
                        msgToSend.Message = msgReceived.Name + ": " + msgReceived.Message;

                        clientSocket.BeginReceive(byteData, 0, byteData.Length, SocketFlags.None, OnReceive, clientSocket);
                        break;

                    case Command.FindGame:
                        if (!findingList.Contains(clientList.First(x => x.Socket == clientSocket)))
                        {
                            findingList.Add(clientList.First(x => x.Socket == clientSocket));
                        }
                        txtLog.Text += string.Format("[{0}] {1} is looking for a game...\r\n", DateTime.Now.ToString("hh:mm:ss"), getClientName(clientSocket));

                        if (findingList.Count > 1)
                        {
                            Game game = new Game();
                            game.player1 = findingList[0];
                            game.player2 = findingList[1];
                            game.player1.Ready = game.player2.Ready = false;
                            findingList.RemoveRange(0, 2);

                            games.Add(game);
                            txtLog.Text += string.Format("[{0}] Connecting {1} with {2}...\r\n", DateTime.Now.ToString("hh:mm:ss"), game.player1.User.name, game.player2.User.name);

                            msgToSend.Command = Command.GameFound;

                            message = msgToSend.ToByte(UserDAO.FindByName(game.player2.User.name));
                            game.player1.Socket.BeginSend(message, 0, message.Length, SocketFlags.None,
                                OnSend, game.player1.Socket);

                            message = msgToSend.ToByte(UserDAO.FindByName(game.player1.User.name));
                            game.player2.Socket.BeginSend(message, 0, message.Length, SocketFlags.None,
                                OnSend, game.player2.Socket);
                        }
                        clientSocket.BeginReceive(byteData, 0, byteData.Length, SocketFlags.None, OnReceive, clientSocket);
                        break;
                    case Command.Ready:
                        //txtLog.Text += "ready\r\n";
                        foreach (var game in games)
                        {
                            if (game.player1.Socket == clientSocket)
                            {
                                game.player1.Ready = true;
                                game.player1.Board = new Board(msgReceived.Message);
                            }
                            else if (game.player2.Socket == clientSocket)
                            {
                                game.player2.Ready = true;
                                game.player2.Board = new Board(msgReceived.Message);
                            }
                            if (game.player1.Ready && game.player2.Ready)
                            {
                                msgToSend.Command = Command.StartGame;
                                message = msgToSend.ToByte();
                                game.player1.Socket.BeginSend(message, 0, message.Length, SocketFlags.None,
                                    OnSend, game.player1.Socket);
                                game.player2.Socket.BeginSend(message, 0, message.Length, SocketFlags.None,
                                    OnSend, game.player2.Socket);

                                msgToSend.Command = Command.Turn;
                                message = msgToSend.ToByte();
                                game.player1.Socket.BeginSend(message, 0, message.Length, SocketFlags.None,
                                    OnSend, game.player1.Socket);
                            }
                        }
                        clientSocket.BeginReceive(byteData, 0, byteData.Length, SocketFlags.None, OnReceive, clientSocket);
                        break;
                    case Command.Shot:
                        foreach (var game in games)
                        {

                            //if (game.player1.Socket == clientSocket)
                            //{
                            //    // send result of the shot back to the client
                            //    switch (game.player2.Board.matrix[msgReceived.X, msgReceived.Y])
                            //    {
                            //        case Cell.Empty:
                            //            msgToSend.Cell = Cell.Miss;
                            //            game.player2.Board.matrix[msgReceived.X, msgReceived.Y] = Cell.Miss;
                            //            break;
                            //        case Cell.Ship:
                            //            msgToSend.Cell = Cell.Hit;
                            //            game.player2.Board.matrix[msgReceived.X, msgReceived.Y] = Cell.Hit;
                            //            if (game.player2.Board.countAliveCells(msgReceived.X, msgReceived.Y) == 0)
                            //            {
                            //                game.player2.Board.matrix[msgReceived.X, msgReceived.Y] = Cell.LastHit;
                            //            }
                            //            break;
                            //    }
                            //    msgToSend.Command = Command.ShotResult;
                            //    message = msgToSend.ToByte();
                            //    clientSocket.BeginSend(message, 0, message.Length, SocketFlags.None,
                            //                OnSend, clientSocket);

                            //    // send to opponent shot
                            //    msgToSend = msgReceived;
                            //    message = msgToSend.ToByte();
                            //    game.player2.Socket.BeginSend(message, 0, message.Length, SocketFlags.None,
                            //        OnSend, game.player2.Socket);

                            //    if (game.player2.Board.IsAllDestroyed()) // clientSocket win
                            //    {
                            //        txtLog.Text += string.Format("{0} win, {1} lose\r\n", getClientName(clientSocket),
                            //            getClientName(game.player2.Socket));
                            //        msgToSend = new Data { Command = Command.Win };
                            //        message = msgToSend.ToByte();
                            //        clientSocket.BeginSend(message, 0, message.Length, SocketFlags.None,
                            //            OnSend, clientSocket);

                            //        msgToSend = new Data { Command = Command.Lose };
                            //        message = msgToSend.ToByte();
                            //        game.player2.Socket.BeginSend(message, 0, message.Length, SocketFlags.None,
                            //            OnSend, game.player2.Socket);

                            //        games.RemoveAll(x => x.player1.Socket == clientSocket || x.player2.Socket == clientSocket);

                            //        game.player1.User.rating += 2;
                            //        game.player1.User.wins++;
                            //        UserDAO.UpdateUser(game.player1.User);
                            //        msgToSend = new Data { Command = Command.User };
                            //        message = msgToSend.ToByte(game.player1.User);
                            //        game.player1.Socket.BeginSend(message, 0, message.Length, SocketFlags.None,
                            //            OnSend, game.player1.Socket);

                            //        game.player2.User.rating += -1;
                            //        game.player2.User.losses++;
                            //        UserDAO.UpdateUser(game.player2.User);
                            //        msgToSend = new Data { Command = Command.User };
                            //        message = msgToSend.ToByte(game.player2.User);
                            //        game.player2.Socket.BeginSend(message, 0, message.Length, SocketFlags.None,
                            //            OnSend, game.player2.Socket);
                            //    }
                            //}
                            //else if (game.player2.Socket == clientSocket)
                            //{
                            //    // send result of the shot back to the client
                            //    switch (game.player1.Board.matrix[msgReceived.X, msgReceived.Y])
                            //    {
                            //        case Cell.Empty:
                            //            msgToSend.Cell = Cell.Miss;
                            //            game.player2.Board.matrix[msgReceived.X, msgReceived.Y] = Cell.Miss;
                            //            break;
                            //        case Cell.Ship:
                            //            msgToSend.Cell = Cell.Hit;
                            //            game.player2.Board.matrix[msgReceived.X, msgReceived.Y] = Cell.Hit;
                            //            if (game.player2.Board.countAliveCells(msgReceived.X, msgReceived.Y) == 0)
                            //            {
                            //                game.player2.Board.matrix[msgReceived.X, msgReceived.Y] = Cell.LastHit;
                            //            }
                            //            break;
                            //    }
                            //    msgToSend.Command = Command.ShotResult;
                            //    message = msgToSend.ToByte();
                            //    clientSocket.BeginSend(message, 0, message.Length, SocketFlags.None,
                            //                OnSend, clientSocket);

                            //    // send to opponent shot
                            //    msgToSend = msgReceived;
                            //    message = msgToSend.ToByte();
                            //    game.player1.Socket.BeginSend(message, 0, message.Length, SocketFlags.None,
                            //        OnSend, game.player1.Socket);

                            //    if (game.player1.Board.IsAllDestroyed()) // clientSocket win
                            //    {
                            //        txtLog.Text += string.Format("{0} win, {1} lose\r\n", getClientName(clientSocket),
                            //            getClientName(game.player1.Socket));
                            //        msgToSend = new Data { Command = Command.Win };
                            //        message = msgToSend.ToByte();
                            //        clientSocket.BeginSend(message, 0, message.Length, SocketFlags.None,
                            //            OnSend, clientSocket);

                            //        msgToSend = new Data { Command = Command.Lose };
                            //        message = msgToSend.ToByte();
                            //        game.player1.Socket.BeginSend(message, 0, message.Length, SocketFlags.None,
                            //            OnSend, game.player1.Socket);

                            //        games.RemoveAll(x => x.player1.Socket == clientSocket || x.player2.Socket == clientSocket);

                            //        game.player2.User.rating += 2;
                            //        game.player2.User.wins++;
                            //        UserDAO.UpdateUser(game.player2.User);
                            //        msgToSend = new Data { Command = Command.User };
                            //        message = msgToSend.ToByte(game.player2.User);
                            //        game.player2.Socket.BeginSend(message, 0, message.Length, SocketFlags.None,
                            //            OnSend, game.player2.Socket);

                            //        game.player1.User.rating += -1;
                            //        game.player1.User.losses++;
                            //        UserDAO.UpdateUser(game.player1.User);
                            //        msgToSend = new Data { Command = Command.User };
                            //        message = msgToSend.ToByte(game.player1.User);
                            //        game.player1.Socket.BeginSend(message, 0, message.Length, SocketFlags.None,
                            //            OnSend, game.player1.Socket);
                            //    }
                            //}
                            ClientInfo ownClient = null;
                            ClientInfo opponentClient = null;
                            if (game.player1.Socket == clientSocket)
                            {
                                ownClient = game.player1;
                                opponentClient = game.player2;
                            }
                            else if (game.player2.Socket == clientSocket)
                            {
                                ownClient = game.player2;
                                opponentClient = game.player1;
                            }

                            if (ownClient == null || opponentClient == null) continue;
                            // send result of the shot back to the client
                            switch (opponentClient.Board.matrix[msgReceived.X, msgReceived.Y])
                            {
                                case Cell.Empty:
                                    msgToSend.Cell = Cell.Miss;
                                    opponentClient.Board.matrix[msgReceived.X, msgReceived.Y] = Cell.Miss;
                                    break;
                                case Cell.Ship:
                                    msgToSend.Cell = Cell.Hit;
                                    opponentClient.Board.matrix[msgReceived.X, msgReceived.Y] = Cell.Hit;
                                    if (opponentClient.Board.CountAliveCells(msgReceived.X, msgReceived.Y) == 0)
                                    {
                                        msgToSend.Cell = Cell.LastHit;
                                    }
                                    break;
                            }
                            msgToSend.Command = Command.ShotResult;
                            message = msgToSend.ToByte();
                            ownClient.Socket.BeginSend(message, 0, message.Length, SocketFlags.None,
                                OnSend, ownClient.Socket);

                            // send to opponent shot
                            msgToSend = msgReceived;
                            message = msgToSend.ToByte();
                            opponentClient.Socket.BeginSend(message, 0, message.Length, SocketFlags.None,
                                OnSend, opponentClient.Socket);

                            if (opponentClient.Board.IsAllDestroyed()) // opponentClient win
                            {
                                txtLog.Text += string.Format("{0} win, {1} lose\r\n",
                                    getClientName(ownClient.Socket),
                                    getClientName(opponentClient.Socket));
                                msgToSend = new Data {Command = Command.Win};
                                message = msgToSend.ToByte();
                                ownClient.Socket.BeginSend(message, 0, message.Length, SocketFlags.None,
                                    OnSend, ownClient.Socket);

                                msgToSend = new Data {Command = Command.Lose};
                                message = msgToSend.ToByte();
                                opponentClient.Socket.BeginSend(message, 0, message.Length, SocketFlags.None,
                                    OnSend, opponentClient.Socket);

                                games.RemoveAll(
                                    x =>
                                        x.player1.Socket == ownClient.Socket ||
                                        x.player2.Socket == ownClient.Socket);

                                ownClient.User.rating += 2;
                                ownClient.User.wins++;
                                UserDAO.UpdateUser(ownClient.User);
                                msgToSend = new Data {Command = Command.User};
                                message = msgToSend.ToByte(ownClient.User);
                                ownClient.Socket.BeginSend(message, 0, message.Length, SocketFlags.None,
                                    OnSend, ownClient.Socket);

                                opponentClient.User.rating += -1;
                                opponentClient.User.losses++;
                                UserDAO.UpdateUser(opponentClient.User);
                                msgToSend = new Data {Command = Command.User};
                                message = msgToSend.ToByte(opponentClient.User);
                                opponentClient.Socket.BeginSend(message, 0, message.Length, SocketFlags.None,
                                    OnSend, opponentClient.Socket);
                            }
                            /*******************/
                        }
                        clientSocket.BeginReceive(byteData, 0, byteData.Length, SocketFlags.None, OnReceive, clientSocket);
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Server: OnReceive", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void OnSend(IAsyncResult ar)
        {
            try
            {
                Socket client = (Socket)ar.AsyncState;
                client.EndSend(ar);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Server: OnSend", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            groupBox_Games.Text = string.Format("Games [{0}]", games.Count.ToString());
            listBox_GamesList.DataSource = games.Select(x => string.Format("{0} - {1}", x.player1.User.name, x.player2.User.name)).ToList();

            groupBox_Queue.Text = string.Format("Queue [{0}]", findingList.Count.ToString());
            listBox_QueueList.DataSource = findingList.Select(x => string.Format("{0}", x.User.name)).ToList();

            groupBox_Online.Text = string.Format("Online [{0}]", clientList.Count.ToString());
            listBox_OnlineList.DataSource = clientList.Select(x => string.Format("{0}", x.User.name)).ToList();
        }
    }
}
