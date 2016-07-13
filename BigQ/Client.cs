﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BigQ
{    
    /// <summary>
    /// Object containing metadata about a client on BigQ.
    /// </summary>
    [Serializable]
    public class Client
    {
        #region Public-Class-Members

        /// <summary>
        /// Contains configuration-related variables for the client.  
        /// </summary>
        public ClientConfiguration Config;

        /// <summary>
        /// The email address associated with the client.  Do not modify directly; used by the server.  
        /// </summary>
        public string Email;

        /// <summary>
        /// The password associated with the client.  Do not modify directly; used by the server.  
        /// </summary>
        public string Password;

        /// <summary>
        /// The GUID associated with the client.  Do not modify directly; used by the server.  
        /// </summary>
        public string ClientGUID;

        /// <summary>
        /// The client's source IP address.  Do not modify directly; used by the server.  
        /// </summary>
        public string SourceIP;

        /// <summary>
        /// The source TCP port number used by the client.  Do not modify directly; used by the server.  
        /// </summary>
        public int SourcePort;

        /// <summary>
        /// The server IP address or hostname to which this client connects.  Do not modify directly; used by the server.  
        /// </summary>
        public string ServerIP;

        /// <summary>
        /// The server TCP port to which this client connects.  Do not modify directly; used by the server.  
        /// </summary>
        public int ServerPort;

        /// <summary>
        /// The UTC timestamp of when this client object was created.
        /// </summary>
        public DateTime CreatedUTC;

        /// <summary>
        /// The UTC timestamp of when this client object was last updated.
        /// </summary>
        public DateTime? UpdatedUTC;

        /// <summary>
        /// Indicates whether or not the client is using raw TCP sockets for messaging.  Do not modify this field.
        /// </summary>
        public bool IsTCP;

        /// <summary>
        /// Indicates whether or not the client is using raw TCP sockets with SSL for messaging.  Do not modify this field.
        /// </summary>
        public bool IsTCPSSL;

        /// <summary>
        /// Indicates whether or not the client is using websockets for messaging.  Do not modify this field.
        /// </summary>
        public bool IsWebsocket;

        /// <summary>
        /// Indicates whether or not the client is using websockets with SSL for messaging.  Do not modify this field.
        /// </summary>
        public bool IsWebsocketSSL;

        /// <summary>
        /// The TcpClient for this client, without SSL.  Provides direct access to the underlying socket.
        /// </summary>
        public TcpClient ClientTCPInterface;

        /// <summary>
        /// The TcpClient for this client, with SSL.  Provides direct access to the underlying socket.
        /// </summary>
        public TcpClient ClientTCPSSLInterface;

        /// <summary>
        /// The SslStream for this client (used only for TCP SSL connections).  Provides direct access to the underlying stream.
        /// </summary>
        public SslStream ClientSSLStream;

        /// <summary>
        /// The HttpListenerContext for this client, without SSL.  Provides direct access to the underlying HTTP listener context object.
        /// </summary>
        public HttpListenerContext ClientHTTPContext;

        /// <summary>
        /// The WebSocketContext for this client, without SSL.  Provides direct access to the underlying WebSocket context.
        /// </summary>
        public WebSocketContext ClientWSContext;

        /// <summary>
        /// The WebSocket for this client, without SSL.  Provides direct access to the underlying WebSocket object.
        /// </summary>
        public WebSocket ClientWSInterface;

        /// <summary>
        /// The HttpListenerContext for this client, with SSL.  Provides direct access to the underlying HTTP listener context object.
        /// </summary>
        public HttpListenerContext ClientHTTPSSLContext;

        /// <summary>
        /// The WebSocketContext for this client, with SSL.  Provides direct access to the underlying WebSocket context.
        /// </summary>
        public WebSocketContext ClientWSSSLContext;

        /// <summary>
        /// The WebSocket for this client, with SSL.  Provides direct access to the underlying WebSocket object.
        /// </summary>
        public WebSocket ClientWSSSLInterface;

        /// <summary>
        /// Indicates whether or not the client is connected to the server.  Do not modify this field.
        /// </summary>
        public bool Connected;

        /// <summary>
        /// Indicates whether or not the client is logged in to the server.  Do not modify this field.
        /// </summary>
        public bool LoggedIn;

        #endregion

        #region Private-Class-Members
        
        private CancellationTokenSource DataReceiverTokenSource = null;
        private CancellationToken DataReceiverToken;
        private CancellationTokenSource CleanupSyncTokenSource = null;
        private CancellationToken CleanupSyncToken;
        private CancellationTokenSource HeartbeatTokenSource = null;
        private CancellationToken HeartbeatToken;
        private ConcurrentDictionary<string, DateTime> SyncRequests;
        private ConcurrentDictionary<string, Message> SyncResponses;
        private X509Certificate2 TCPSSLCertificate;
        private X509Certificate2Collection TCPSSLCertificateCollection = null;
        
        #endregion

        #region Public-Delegates

        /// <summary>
        /// Delegate method called when an asynchronous message is received.
        /// </summary>
        public Func<Message, bool> AsyncMessageReceived;

        /// <summary>
        /// Delegate method called when a synchronous message is received.
        /// </summary>
        public Func<Message, byte[]> SyncMessageReceived;

        /// <summary>
        /// Delegate method called with the server connection is severed.
        /// </summary>
        public Func<bool> ServerDisconnected;

        /// <summary>
        /// Delegate method called when the client desires to send a log message.
        /// </summary>
        public Func<string, bool> LogMessage;

        #endregion

        #region Public-Constructors

        /// <summary>
        /// This constructor is used by BigQServer.  Do not use it in client applications!
        /// </summary>
        public Client()
        {
        }

        /// <summary>
        /// Start an instance of the BigQ client process.
        /// </summary>
        /// <param name="configFile">The full path and filename of the configuration file.  Leave null for a default configuration.</param>
        public Client(string configFile)
        {
            #region Load-and-Validate-Config

            CreatedUTC = DateTime.Now.ToUniversalTime();
            Config = null;

            if (String.IsNullOrEmpty(configFile))
            {
                Config = ClientConfiguration.DefaultConfig();
            }
            else
            {
                Config = ClientConfiguration.LoadConfig(configFile);
            }

            if (Config == null) throw new Exception("Unable to initialize configuration.");

            Config.ValidateConfig();

            #endregion

            #region Set-Class-Variables

            if (String.IsNullOrEmpty(Config.GUID)) Config.GUID = Guid.NewGuid().ToString();
            if (String.IsNullOrEmpty(Config.Email)) Config.Email = Config.GUID;
            if (String.IsNullOrEmpty(Config.Password)) Config.Password = Config.GUID;

            Email = Config.Email;
            ClientGUID = Config.GUID;
            Password = Config.Password;
            SourceIP = "";
            SourcePort = 0;
            ServerIP = "";
            ServerPort = 0;

            SyncRequests = new ConcurrentDictionary<string, DateTime>();
            SyncResponses = new ConcurrentDictionary<string, Message>();
            Connected = false;
            LoggedIn = false;

            #endregion

            #region Set-Delegates-to-Null

            AsyncMessageReceived = null;
            SyncMessageReceived = null;
            ServerDisconnected = null;
            LogMessage = null;

            #endregion

            #region Accept-SSL-Certificates

            if (Config.AcceptInvalidSSLCerts) ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };

            #endregion

            #region Start-Client

            if (Config.TcpServer.Enable)
            {
                #region Start-TCP-Server

                //
                // see https://social.msdn.microsoft.com/Forums/vstudio/en-US/2281199d-cd28-4b5c-95dc-5a888a6da30d/tcpclientconnect-timeout?forum=csharpgeneral
                // removed using statement since resources are managed by the caller
                //
                ServerIP = Config.TcpServer.IP;
                ServerPort = Config.TcpServer.Port;

                ClientTCPInterface = new TcpClient();
                IAsyncResult ar = ClientTCPInterface.BeginConnect(Config.TcpServer.IP, Config.TcpServer.Port, null, null);
                WaitHandle wh = ar.AsyncWaitHandle;

                try
                {
                    if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5), false))
                    {
                        ClientTCPInterface.Close();
                        throw new TimeoutException("Timeout connecting to " + Config.TcpServer.IP + ":" + Config.TcpServer.Port);
                    }

                    ClientTCPInterface.EndConnect(ar);

                    SourceIP = ((IPEndPoint)ClientTCPInterface.Client.LocalEndPoint).Address.ToString();
                    SourcePort = ((IPEndPoint)ClientTCPInterface.Client.LocalEndPoint).Port;
                }
                catch (Exception e)
                {
                    throw e;
                }
                finally
                {
                    wh.Close();
                }

                Connected = true;

                #endregion
            }
            else if (Config.TcpSSLServer.Enable)
            {
                #region Start-TCP-SSL-Server

                //
                // Setup the SSL certificate and certificate collection
                //
                TCPSSLCertificate = null;
                TCPSSLCertificateCollection = new X509Certificate2Collection();
                if (String.IsNullOrEmpty(Config.TcpSSLServer.P12CertPassword))
                {
                    TCPSSLCertificate = new X509Certificate2(Config.TcpSSLServer.P12CertFile);
                    TCPSSLCertificateCollection.Add(new X509Certificate2(Config.TcpSSLServer.P12CertFile));
                }
                else
                {
                    TCPSSLCertificate = new X509Certificate2(Config.TcpSSLServer.P12CertFile, Config.TcpSSLServer.P12CertPassword);
                    TCPSSLCertificateCollection.Add(new X509Certificate2(Config.TcpSSLServer.P12CertFile, Config.TcpSSLServer.P12CertPassword));
                }

                ServerIP = Config.TcpSSLServer.IP;
                ServerPort = Config.TcpSSLServer.Port;
                
                ClientTCPSSLInterface = new TcpClient();
                IAsyncResult ar = ClientTCPSSLInterface.BeginConnect(Config.TcpSSLServer.IP, Config.TcpSSLServer.Port, null, null);
                WaitHandle wh = ar.AsyncWaitHandle;

                try
                {
                    if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5), false))
                    {
                        ClientTCPSSLInterface.Close();
                        throw new TimeoutException("Timeout connecting to " + Config.TcpSSLServer.IP + ":" + Config.TcpSSLServer.Port);
                    }

                    ClientTCPSSLInterface.EndConnect(ar);
                    SourceIP = ((IPEndPoint)ClientTCPSSLInterface.Client.LocalEndPoint).Address.ToString();
                    SourcePort = ((IPEndPoint)ClientTCPSSLInterface.Client.LocalEndPoint).Port;

                    //
                    // Setup SSL and authenticate
                    //
                    if (Config.AcceptInvalidSSLCerts)
                    {
                        ClientSSLStream = new SslStream(ClientTCPSSLInterface.GetStream(), false, new RemoteCertificateValidationCallback(ValidateCert));
                    }
                    else
                    {
                        //
                        // do not accept invalid SSL certificates
                        //
                        ClientSSLStream = new SslStream(ClientTCPSSLInterface.GetStream(), false);
                    }
                    
                    ClientSSLStream.AuthenticateAsClient(Config.TcpSSLServer.IP, TCPSSLCertificateCollection, SslProtocols.Default, true);
                }
                catch (Exception e)
                {
                    throw e;
                }
                finally
                {
                    wh.Close();
                }

                Connected = true;

                #endregion
            }
            else
            {
                #region Unknown-Server

                throw new Exception("Exactly one server must be enabled in the configuration file.");

                #endregion
            }

            #endregion

            #region Stop-Existing-Tasks

            if (DataReceiverTokenSource != null) DataReceiverTokenSource.Cancel();
            if (CleanupSyncTokenSource != null) CleanupSyncTokenSource.Cancel();
            if (HeartbeatTokenSource != null) HeartbeatTokenSource.Cancel();

            #endregion

            #region Start-Tasks

            DataReceiverTokenSource = new CancellationTokenSource();
            DataReceiverToken = DataReceiverTokenSource.Token;

            if (Config.TcpServer.Enable) Task.Run(() => TCPDataReceiver(), DataReceiverToken);
            else if (Config.TcpSSLServer.Enable) Task.Run(() => TCPSSLDataReceiver(), DataReceiverToken);
            else
            {
                throw new Exception("Exactly one server must be enabled in the configuration file.");
            }

            CleanupSyncTokenSource = new CancellationTokenSource();
            CleanupSyncToken = CleanupSyncTokenSource.Token;
            Task.Run(() => CleanupSyncRequests(), CleanupSyncToken);

            //
            //
            //
            // do not start heartbeat until successful login
            // design goal: server needs to free up connections fast
            // client just needs to keep socket open
            //
            //
            //
            // HeartbeatTokenSource = new CancellationTokenSource();
            // HeartbeatToken = HeartbeatTokenSource.Token;
            // Task.Run(() => TCPHeartbeatManager());

            #endregion
        }
        
        #endregion

        #region Public-Methods

        /// <summary>
        /// Sends a message; makes the assumption that you have populated the object fully and correctly.  In general, this method should not be used.
        /// </summary>
        /// <param name="message">The populated message object to send.</param>
        /// <returns></returns>
        public bool SendRawMessage(Message message)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            try
            {
                if (message == null) throw new ArgumentNullException("message");
                if (Config.TcpServer.Enable) return TCPDataSender(message);
                else if (Config.TcpSSLServer.Enable) return TCPSSLDataSender(message);
                else
                {
                    Log("*** SendRawMessage no server enabled");
                    return false;
                }
            }
            finally
            {
                sw.Stop();
                if (Config.Debug.Enable && Config.Debug.MsgResponseTime) Log("SendRawMessage " + sw.Elapsed.TotalMilliseconds + "ms");
            }
        }

        /// <summary>
        /// Generates an echo request to the server, which should result in an asynchronous echo response.  Typically used to validate connectivity.
        /// </summary>
        /// <returns></returns>
        public bool Echo()
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            try
            {
                Message request = new Message();
                request.Email = Email;
                request.Password = Password;
                request.Command = "Echo";
                request.CreatedUTC = DateTime.Now.ToUniversalTime();
                request.MessageId = Guid.NewGuid().ToString();
                request.SenderGuid = ClientGUID;
                request.RecipientGuid = "00000000-0000-0000-0000-000000000000";
                request.SyncRequest = true;
                request.ChannelGuid = null;
                request.Data = null;
                return SendRawMessage(request);
            }
            finally
            {
                sw.Stop();
                if (Config.Debug.Enable && Config.Debug.MsgResponseTime) Log("Echo " + sw.Elapsed.TotalMilliseconds + "ms");
            }
        }

        /// <summary>
        /// Login to the server.
        /// </summary>
        /// <param name="response">The full response message received from the server.</param>
        /// <returns>Boolean indicating if the login was successful.</returns>
        public bool Login(out Message response)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            try
            {
                response = null;
                Message request = new Message();
                request.Email = Email;
                request.Password = Password;
                request.Command = "Login";
                request.CreatedUTC = DateTime.Now.ToUniversalTime();
                request.MessageId = Guid.NewGuid().ToString();
                request.SenderGuid = ClientGUID;
                request.RecipientGuid = "00000000-0000-0000-0000-000000000000";
                request.SyncRequest = true;
                request.ChannelGuid = null;
                request.Data = null;

                if (!SendServerMessageSync(request, out response))
                {
                    Log("*** Login unable to retrieve server response");
                    return false;
                }

                if (response == null)
                {
                    Log("*** Login null response from server");
                    return false;
                }

                if (!Helper.IsTrue(response.Success))
                {
                    Log("*** Login failed with response data " + response.Data.ToString());
                    return false;
                }
                else
                {
                    LoggedIn = true;

                    // stop existing heartbeat thread
                    if (HeartbeatTokenSource != null) HeartbeatTokenSource.Cancel();

                    // start new heartbeat thread
                    HeartbeatTokenSource = new CancellationTokenSource();
                    HeartbeatToken = HeartbeatTokenSource.Token;
                    Task.Run(() => HeartbeatManager(), HeartbeatToken);

                    return true;
                }
            }
            finally
            {
                sw.Stop();
                if (Config.Debug.Enable && Config.Debug.MsgResponseTime) Log("Login " + sw.Elapsed.TotalMilliseconds + "ms");
            }
        }

        /// <summary>
        /// Retrieve a list of all clients on the server.
        /// </summary>
        /// <param name="response">The full response message received from the server.</param>
        /// <param name="clients">The list of clients received from the server.</param>
        /// <returns>Boolean indicating whether or not the call succeeded.</returns>
        public bool ListClients(out Message response, out List<Client> clients)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            try
            {
                response = null;
                clients = null;

                Message request = new Message();
                request.Email = Email;
                request.Password = Password;
                request.Command = "ListClients";
                request.CreatedUTC = DateTime.Now.ToUniversalTime();
                request.MessageId = Guid.NewGuid().ToString();
                request.SenderGuid = ClientGUID;
                request.RecipientGuid = "00000000-0000-0000-0000-000000000000";
                request.SyncRequest = true;
                request.ChannelGuid = null;
                request.Data = null;

                if (!SendServerMessageSync(request, out response))
                {
                    Log("*** ListClients unable to retrieve server response");
                    return false;
                }

                if (response == null)
                {
                    Log("*** ListClients null response from server");
                    return false;
                }

                if (!Helper.IsTrue(response.Success))
                {
                    Log("*** ListClients failed with response data " + response.Data.ToString());
                    return false;
                }
                else
                {
                    if (response.Data != null)
                    {
                        if (Config.Debug.Enable && Config.Debug.MsgResponseTime) Log("ListClients deserialize start " + sw.Elapsed.TotalMilliseconds + "ms");
                        clients = Helper.DeserializeJson<List<Client>>(response.Data, false);
                    }
                    return true;
                }
            }
            finally
            {
                sw.Stop();
                if (Config.Debug.Enable && Config.Debug.MsgResponseTime) Log("ListClients " + sw.Elapsed.TotalMilliseconds + "ms");
            }
        }

        /// <summary>
        /// Retrieve a list of all channels on the server.
        /// </summary>
        /// <param name="response">The full response message received from the server.</param>
        /// <param name="channels">The list of channels received from the server.</param>
        /// <returns>Boolean indicating whether or not the call succeeded.</returns>
        public bool ListChannels(out Message response, out List<Channel> channels)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            try
            {
                response = null;
                channels = null;

                Message request = new Message();
                request.Email = Email;
                request.Password = Password;
                request.Command = "ListChannels";
                request.CreatedUTC = DateTime.Now.ToUniversalTime();
                request.MessageId = Guid.NewGuid().ToString();
                request.SenderGuid = ClientGUID;
                request.RecipientGuid = "00000000-0000-0000-0000-000000000000";
                request.SyncRequest = true;
                request.ChannelGuid = null;
                request.Data = null;

                if (!SendServerMessageSync(request, out response))
                {
                    Log("*** ListChannels unable to retrieve server response");
                    return false;
                }

                if (response == null)
                {
                    Log("*** ListChannels null response from server");
                    return false;
                }

                if (!Helper.IsTrue(response.Success))
                {
                    Log("*** ListChannels failed with response data " + response.Data.ToString());
                    return false;
                }
                else
                {
                    if (response.Data != null)
                    {
                        if (Config.Debug.Enable && Config.Debug.MsgResponseTime) Log("ListChanels deserialize start " + sw.Elapsed.TotalMilliseconds + "ms");
                        channels = Helper.DeserializeJson<List<Channel>>(response.Data, false);
                    }
                    return true;
                }
            }
            finally
            {
                sw.Stop();
                if (Config.Debug.Enable && Config.Debug.MsgResponseTime) Log("ListChannels " + sw.Elapsed.TotalMilliseconds + "ms");
            }
        }

        /// <summary>
        /// Retrieve a list of all subscribers in a specific channel.
        /// </summary>
        /// <param name="guid">The GUID of the channel.</param>
        /// <param name="response">The full response message received from the server.</param>
        /// <param name="clients">The list of clients subscribed to the specified channel on the server.</param>
        /// <returns>Boolean indicating whether or not the call succeeded.</returns>
        public bool ListChannelSubscribers(string guid, out Message response, out List<Client> clients)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            try
            {
                response = null;
                clients = null;

                if (String.IsNullOrEmpty(guid)) throw new ArgumentNullException("guid");

                Message request = new Message();
                request.Email = Email;
                request.Password = Password;
                request.Command = "ListChannelSubscribers";
                request.CreatedUTC = DateTime.Now.ToUniversalTime();
                request.MessageId = Guid.NewGuid().ToString();
                request.SenderGuid = ClientGUID;
                request.RecipientGuid = "00000000-0000-0000-0000-000000000000";
                request.SyncRequest = true;
                request.ChannelGuid = guid;
                request.Data = null;

                if (!SendServerMessageSync(request, out response))
                {
                    Log("*** ListChannelSubscribers unable to retrieve server response");
                    return false;
                }

                if (response == null)
                {
                    Log("*** ListChannelSubscribers null response from server");
                    return false;
                }

                if (!Helper.IsTrue(response.Success))
                {
                    Log("*** ListChannelSubscribers failed with response data " + response.Data.ToString());
                    return false;
                }
                else
                {
                    if (response.Data != null)
                    {
                        if (Config.Debug.Enable && Config.Debug.MsgResponseTime) Log("ListChannelSubscribers deserialize start " + sw.Elapsed.TotalMilliseconds + "ms");
                        clients = Helper.DeserializeJson<List<Client>>(response.Data, false);
                    }
                    return true;
                }
            }
            finally
            {
                sw.Stop();
                if (Config.Debug.Enable && Config.Debug.MsgResponseTime) Log("ListChannelSubscribers " + sw.Elapsed.TotalMilliseconds + "ms");
            }
        }

        /// <summary>
        /// Join a specified channel on the server.
        /// </summary>
        /// <param name="guid">The GUID of the channel.</param>
        /// <param name="response">The full response message received from the server.</param>
        /// <returns>Boolean indicating whether or not the call succeeded.</returns>
        public bool JoinChannel(string guid, out Message response)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            try
            {
                response = null;
                if (String.IsNullOrEmpty(guid)) throw new ArgumentNullException("guid");

                Message request = new Message();
                request.Email = Email;
                request.Password = Password;
                request.Command = "JoinChannel";
                request.CreatedUTC = DateTime.Now.ToUniversalTime();
                request.MessageId = Guid.NewGuid().ToString();
                request.SenderGuid = ClientGUID;
                request.RecipientGuid = "00000000-0000-0000-0000-000000000000";
                request.SyncRequest = true;
                request.ChannelGuid = guid;
                request.Data = null;

                if (!SendServerMessageSync(request, out response))
                {
                    Log("*** JoinChannel unable to retrieve server response");
                    return false;
                }

                if (response == null)
                {
                    Log("*** JoinChannel null response from server");
                    return false;
                }

                if (!Helper.IsTrue(response.Success))
                {
                    Log("*** JoinChannel failed with response data " + response.Data.ToString());
                    return false;
                }
                else
                {
                    return true;
                }
            }
            finally
            {
                sw.Stop();
                if (Config.Debug.Enable && Config.Debug.MsgResponseTime) Log("JoinChannel " + sw.Elapsed.TotalMilliseconds + "ms");
            }
        }

        /// <summary>
        /// Leave a channel on the server to which you are joined.
        /// </summary>
        /// <param name="guid">The GUID of the channel.</param>
        /// <param name="response">The full response message received from the server.</param>
        /// <returns>Boolean indicating whether or not the call succeeded.</returns>
        public bool LeaveChannel(string guid, out Message response)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            try
            {
                response = null;
                if (String.IsNullOrEmpty(guid)) throw new ArgumentNullException("guid");

                Message request = new Message();
                request.Email = Email;
                request.Password = Password;
                request.Command = "LeaveChannel";
                request.CreatedUTC = DateTime.Now.ToUniversalTime();
                request.MessageId = Guid.NewGuid().ToString();
                request.SenderGuid = ClientGUID;
                request.RecipientGuid = "00000000-0000-0000-0000-000000000000";
                request.SyncRequest = true;
                request.ChannelGuid = guid;
                request.Data = null;

                if (!SendServerMessageSync(request, out response))
                {
                    Log("*** LeaveChannel unable to retrieve server response");
                    return false;
                }

                if (response == null)
                {
                    Log("*** LeaveChannel null response from server");
                    return false;
                }

                if (!Helper.IsTrue(response.Success))
                {
                    Log("*** LeaveChannel failed with response data " + response.Data.ToString());
                    return false;
                }
                else
                {
                    return true;
                }
            }
            finally
            {
                sw.Stop();
                if (Config.Debug.Enable && Config.Debug.MsgResponseTime) Log("LeaveChannel " + sw.Elapsed.TotalMilliseconds + "ms");
            }
        }

        /// <summary>
        /// Create a channel on the server.
        /// </summary>
        /// <param name="name">The name you wish to assign to the new channel.</param>
        /// <param name="priv">Whether or not the channel is private (1) or public (0).</param>
        /// <param name="response">The full response message received from the server.</param>
        /// <returns>Boolean indicating whether or not the call succeeded.</returns>
        public bool CreateChannel(string name, int priv, out Message response)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            try
            {
                response = null;
                if (String.IsNullOrEmpty(name)) throw new ArgumentNullException("name");
                if (priv != 0 && priv != 1) throw new ArgumentOutOfRangeException("Value for priv must be 0 or 1");

                Channel CurrentChannel = new Channel();
                CurrentChannel.ChannelName = name;
                CurrentChannel.OwnerGuid = ClientGUID;
                CurrentChannel.Guid = Guid.NewGuid().ToString();
                CurrentChannel.Private = priv;

                Message request = new Message();
                request.Email = Email;
                request.Password = Password;
                request.Command = "CreateChannel";
                request.CreatedUTC = DateTime.Now.ToUniversalTime();
                request.MessageId = Guid.NewGuid().ToString();
                request.SenderGuid = ClientGUID;
                request.RecipientGuid = "00000000-0000-0000-0000-000000000000";
                request.SyncRequest = true;
                request.ChannelGuid = CurrentChannel.Guid;
                request.Data = Encoding.UTF8.GetBytes(Helper.SerializeJson(CurrentChannel));

                if (!SendServerMessageSync(request, out response))
                {
                    Log("*** CreateChannel unable to retrieve server response");
                    return false;
                }

                if (response == null)
                {
                    Log("*** CreateChannel null response from server");
                    return false;
                }

                if (!Helper.IsTrue(response.Success))
                {
                    Log("*** CreateChannel failed with response data " + response.Data.ToString());
                    return false;
                }
                else
                {
                    return true;
                }
            }
            finally
            {
                sw.Stop();
                if (Config.Debug.Enable && Config.Debug.MsgResponseTime) Log("CreateChannel " + sw.Elapsed.TotalMilliseconds + "ms");
            }
        }

        /// <summary>
        /// Delete a channel you own on the server.
        /// </summary>
        /// <param name="guid">The GUID of the channel.</param>
        /// <param name="response">The full response message received from the server.</param>
        /// <returns>Boolean indicating whether or not the call succeeded.</returns>
        public bool DeleteChannel(string guid, out Message response)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            try
            {
                response = null;
                if (String.IsNullOrEmpty(guid)) throw new ArgumentNullException("guid");

                Message request = new Message();
                request.Email = Email;
                request.Password = Password;
                request.Command = "DeleteChannel";
                request.CreatedUTC = DateTime.Now.ToUniversalTime();
                request.MessageId = Guid.NewGuid().ToString();
                request.SenderGuid = ClientGUID;
                request.RecipientGuid = "00000000-0000-0000-0000-000000000000";
                request.SyncRequest = true;
                request.ChannelGuid = null;
                request.Data = null;

                if (!SendServerMessageSync(request, out response))
                {
                    Log("*** DeleteChannel unable to retrieve server response");
                    return false;
                }

                if (response == null)
                {
                    Log("*** DeleteChannel null response from server");
                    return false;
                }

                if (!Helper.IsTrue(response.Success))
                {
                    Log("*** DeleteChannel failed with response data " + response.Data.ToString());
                    return false;
                }
                else
                {
                    return true;
                }
            }
            finally
            {
                sw.Stop();
                if (Config.Debug.Enable && Config.Debug.MsgResponseTime) Log("DeleteChannel " + sw.Elapsed.TotalMilliseconds + "ms");
            }
        }

        /// <summary>
        /// Send a private message to another user on this server asynchronously.
        /// </summary>
        /// <param name="guid">The GUID of the recipient user.</param>
        /// <param name="data">The data you wish to send to the user (string or byte array).</param>
        /// <returns>Boolean indicating whether or not the call succeeded.</returns>
        public bool SendPrivateMessageAsync(string guid, string data)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            try
            {
                if (String.IsNullOrEmpty(guid)) throw new ArgumentNullException("guid");
                if (String.IsNullOrEmpty(data)) throw new ArgumentNullException("data");
                return SendPrivateMessageAsync(guid, Encoding.UTF8.GetBytes(data));
            }
            finally
            {
                sw.Stop();
                if (Config.Debug.Enable && Config.Debug.MsgResponseTime) Log("SendPrivateMessageAsync (string) " + sw.Elapsed.TotalMilliseconds + "ms");
            }
        }

        /// <summary>
        /// Send a private message to another user on this server asynchronously.
        /// </summary>
        /// <param name="guid">The GUID of the recipient user.</param>
        /// <param name="data">The data you wish to send to the user (string or byte array).</param>
        /// <returns>Boolean indicating whether or not the call succeeded.</returns>
        public bool SendPrivateMessageAsync(string guid, byte[] data)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            try
            {
                if (String.IsNullOrEmpty(guid)) throw new ArgumentNullException("guid");
                if (data == null) throw new ArgumentNullException("data");

                Message CurrentMessage = new Message();
                CurrentMessage.Email = Email;
                CurrentMessage.Password = Password;
                CurrentMessage.Command = null;
                CurrentMessage.CreatedUTC = DateTime.Now.ToUniversalTime();
                CurrentMessage.MessageId = Guid.NewGuid().ToString();
                CurrentMessage.SenderGuid = ClientGUID;
                CurrentMessage.RecipientGuid = guid;
                CurrentMessage.ChannelGuid = null;
                CurrentMessage.Data = data;
                return SendRawMessage(CurrentMessage);
            }
            finally
            {
                sw.Stop();
                if (Config.Debug.Enable && Config.Debug.MsgResponseTime) Log("SendPrivateMessageAsync (byte) " + sw.Elapsed.TotalMilliseconds + "ms");
            }
        }

        /// <summary>
        /// Send a private message to another user on this server synchronously.
        /// </summary>
        /// <param name="guid">The GUID of the recipient user.</param>
        /// <param name="data">The data you wish to send to the user (string or byte array).</param>
        /// <param name="response">The full response message received from the server.</param>
        /// <returns>Boolean indicating whether or not the call succeeded.</returns>
        public bool SendPrivateMessageSync(string guid, string data, out Message response)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            try
            {
                if (String.IsNullOrEmpty(guid)) throw new ArgumentNullException("guid");
                if (String.IsNullOrEmpty(data)) throw new ArgumentNullException("data");
                return SendPrivateMessageSync(guid, Encoding.UTF8.GetBytes(data), out response);
            }
            finally
            {
                sw.Stop();
                if (Config.Debug.Enable && Config.Debug.MsgResponseTime) Log("SendPrivateMessageSync (string) " + sw.Elapsed.TotalMilliseconds + "ms");
            }
        }

        /// <summary>
        /// Send a private message to another user on this server synchronously.
        /// </summary>
        /// <param name="guid">The GUID of the recipient user.</param>
        /// <param name="data">The data you wish to send to the user (string or byte array).</param>
        /// <param name="response">The full response message received from the server.</param>
        /// <returns>Boolean indicating whether or not the call succeeded.</returns>
        public bool SendPrivateMessageSync(string guid, byte[] data, out Message response)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            Message CurrentMessage = new Message();
            
            try
            {
                response = null;

                if (String.IsNullOrEmpty(guid)) throw new ArgumentNullException("guid");
                if (data == null) throw new ArgumentNullException("data");

                CurrentMessage = new Message();
                CurrentMessage.Email = Email;
                CurrentMessage.Password = Password;
                CurrentMessage.Command = null;
                CurrentMessage.CreatedUTC = DateTime.Now.ToUniversalTime();
                CurrentMessage.MessageId = Guid.NewGuid().ToString();
                CurrentMessage.SenderGuid = ClientGUID;
                CurrentMessage.RecipientGuid = guid;
                CurrentMessage.ChannelGuid = null;
                CurrentMessage.SyncRequest = true;
                CurrentMessage.Data = data;

                if (!AddSyncRequest(CurrentMessage.MessageId))
                {
                    Log("*** SendPrivateMessageSync unable to register sync request GUID " + CurrentMessage.MessageId);
                    return false;
                }

                if (!SendRawMessage(CurrentMessage))
                {
                    Log("*** SendPrivateMessage unable to send message GUID " + CurrentMessage.MessageId + " to recipient " + CurrentMessage.RecipientGuid);
                    return false;
                }

                Message ResponseMessage = new Message();
                if (!GetSyncResponse(CurrentMessage.MessageId, out ResponseMessage))
                {
                    Log("*** SendPrivateMessage unable to get response for message GUID " + CurrentMessage.MessageId);
                    return false;
                }

                if (ResponseMessage != null)
                {
                    response = ResponseMessage;
                }
                return true;
            }
            finally
            {
                sw.Stop();
                if (Config.Debug.Enable && Config.Debug.MsgResponseTime) Log("SendPrivateMessageSync (byte) " + sw.Elapsed.TotalMilliseconds + "ms");
            }
        }

        /// <summary>
        /// Send a private message to the server asynchronously.
        /// </summary>
        /// <param name="request">The message object you wish to send to the server.</param>
        /// <returns>Boolean indicating whether or not the call succeeded.</returns>
        public bool SendServerMessageAsync(Message request)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            try
            {
                if (request == null) throw new ArgumentNullException("request");
                request.RecipientGuid = "00000000-0000-0000-0000-000000000000";
                return SendRawMessage(request);
            }
            finally
            {
                sw.Stop();
                if (Config.Debug.Enable && Config.Debug.MsgResponseTime) Log("SendServerMessageAsync " + sw.Elapsed.TotalMilliseconds + "ms");
            }
        }

        /// <summary>
        /// Send a private message to the server synchronously.
        /// </summary>
        /// <param name="request">The message object you wish to send to the server.</param>
        /// <param name="response">The full response message received from the server.</param>
        /// <returns>Boolean indicating whether or not the call succeeded.</returns>
        public bool SendServerMessageSync(Message request, out Message response)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            
            try
            {
                response = null;
            
                if (request == null) throw new ArgumentNullException("request");
                if (String.IsNullOrEmpty(request.MessageId)) request.MessageId = Guid.NewGuid().ToString();
                request.SyncRequest = true;
                request.RecipientGuid = "00000000-0000-0000-0000-000000000000";

                if (!AddSyncRequest(request.MessageId))
                {
                    Log("*** SendServerMessageSync unable to register sync request GUID " + request.MessageId);
                    return false;
                }
                else
                {
                    // Log("SendServerMessageSync registered sync request GUID " + request.MessageId);
                }

                if (!SendRawMessage(request))
                {
                    Log("*** SendServerMessageSync unable to send message GUID " + request.MessageId + " to server");
                    return false;
                }
                else
                {
                    // Log("SendServerMessageSync sent message GUID " + request.MessageId + " to server");
                }

                Message ResponseMessage = new Message();
                if (!GetSyncResponse(request.MessageId, out ResponseMessage))
                {
                    Log("*** SendServerMessageSync unable to get response for message GUID " + request.MessageId);
                    return false;
                }
                else
                {
                    // Log("SendServerMessageSync received response for message GUID " + request.MessageId);
                }
                
                if (ResponseMessage != null) response = ResponseMessage;
                return true;
            }
            finally
            {
                sw.Stop();
                if (Config.Debug.Enable && Config.Debug.MsgResponseTime) Log("SendServerMessageSync " + sw.Elapsed.TotalMilliseconds + "ms");
            }
        }

        /// <summary>
        /// Send a message to a channel, which is in turn sent to each subscriber of that channel.
        /// </summary>
        /// <param name="guid">The GUID of the channel.</param>
        /// <param name="data">The data you wish to send to the channel (string or byte array).</param>
        /// <returns>Boolean indicating whether or not the call succeeded.</returns>
        public bool SendChannelMessage(string guid, string data)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            try
            {
                if (String.IsNullOrEmpty(guid)) throw new ArgumentNullException("guid");
                if (String.IsNullOrEmpty(data)) throw new ArgumentNullException("data");
                return SendChannelMessage(guid, Encoding.UTF8.GetBytes(data));
            }
            finally
            {
                sw.Stop();
                if (Config.Debug.Enable && Config.Debug.MsgResponseTime) Log("SendChannelMessage (string) " + sw.Elapsed.TotalMilliseconds + "ms");
            }
        }

        /// <summary>
        /// Send a message to a channel, which is in turn sent to each subscriber of that channel.
        /// </summary>
        /// <param name="guid">The GUID of the channel.</param>
        /// <param name="data">The data you wish to send to the channel (string or byte array).</param>
        /// <returns>Boolean indicating whether or not the call succeeded.</returns>
        public bool SendChannelMessage(string guid, byte[] data)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            try
            {
                if (String.IsNullOrEmpty(guid)) throw new ArgumentNullException("guid");
                if (data == null) throw new ArgumentNullException("data");

                Message CurrentMessage = new Message();
                CurrentMessage.Email = Email;
                CurrentMessage.Password = Password;
                CurrentMessage.Command = null;
                CurrentMessage.CreatedUTC = DateTime.Now.ToUniversalTime();
                CurrentMessage.MessageId = Guid.NewGuid().ToString();
                CurrentMessage.SenderGuid = ClientGUID;
                CurrentMessage.RecipientGuid = null;
                CurrentMessage.ChannelGuid = guid;
                CurrentMessage.Data = data;
                return SendRawMessage(CurrentMessage);
            }
            finally
            {
                sw.Stop();
                if (Config.Debug.Enable && Config.Debug.MsgResponseTime) Log("SendChannelMessage (byte) " + sw.Elapsed.TotalMilliseconds + "ms");
            }
        }

        /// <summary>
        /// Retrieve the list of synchronous requests awaiting responses.
        /// </summary>
        /// <param name="response">A dictionary containing the GUID of the synchronous request (key) and the timestamp it was sent (value).</param>
        /// <returns>Boolean indicating whether or not the call succeeded.</returns>
        public bool PendingSyncRequests(out Dictionary<string, DateTime> response)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            try
            {
                response = null;
                if (SyncRequests == null) return true;
                if (SyncRequests.Count < 1) return true;

                response = SyncRequests.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                return true;
            }
            finally
            {
                sw.Stop();
                if (Config.Debug.Enable && Config.Debug.MsgResponseTime) Log("PendingSyncRequests " + sw.Elapsed.TotalMilliseconds + "ms");
            }
        }

        /// <summary>
        /// Discern whether or not a given client is connected.
        /// </summary>
        /// <param name="guid">The GUID of the client.</param>
        /// <param name="response">The full response message received from the server.</param>
        /// <returns>Boolean indicating whether or not the call succeeded.</returns>
        public bool IsClientConnected(string guid, out Message response)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            try
            {
                response = null;

                Message request = new Message();
                request.Email = Email;
                request.Password = Password;
                request.Command = "IsClientConnected";
                request.CreatedUTC = DateTime.Now.ToUniversalTime();
                request.MessageId = Guid.NewGuid().ToString();
                request.SenderGuid = ClientGUID;
                request.RecipientGuid = "00000000-0000-0000-0000-000000000000";
                request.SyncRequest = true;
                request.ChannelGuid = null;
                request.Data = Encoding.UTF8.GetBytes(guid);

                if (!SendServerMessageSync(request, out response))
                {
                    Log("*** ListClients unable to retrieve server response");
                    return false;
                }

                if (response == null)
                {
                    Log("*** ListClients null response from server");
                    return false;
                }

                if (!Helper.IsTrue(response.Success))
                {
                    Log("*** ListClients failed with response data " + response.Data.ToString());
                    return false;
                }
                else
                {
                    if (response.Data != null)
                    {
                        return Helper.IsTrue(Encoding.UTF8.GetString(response.Data));
                    }
                    return false;
                }
            }
            finally
            {
                sw.Stop();
                if (Config.Debug.Enable && Config.Debug.MsgResponseTime) Log("IsClientConnected " + sw.Elapsed.TotalMilliseconds + "ms");
            }
        }

        /// <summary>
        /// Retrieve a structured string containing the IP address and port of the client in the format of 10.1.142.12:31763.
        /// </summary>
        /// <returns>Formatted string containing the IP address and port of the client.</returns>
        public string IpPort()
        {
            return SourceIP + ":" + SourcePort;
        }

        /// <summary>
        /// Close and dispose of client resources.
        /// </summary>
        public void Close()
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            try
            {
                if (DataReceiverTokenSource != null) DataReceiverTokenSource.Cancel();
                if (CleanupSyncTokenSource != null) CleanupSyncTokenSource.Cancel();
                if (HeartbeatTokenSource != null) HeartbeatTokenSource.Cancel();

                if (ClientTCPInterface != null)
                {
                    if (ClientTCPInterface.Connected)
                    {
                        //
                        // close the TCP stream
                        //
                        if (ClientTCPInterface.GetStream() != null)
                        {
                            ClientTCPInterface.GetStream().Close();
                        }
                    }

                    // 
                    // close the client
                    //

                    if (ClientTCPInterface != null) ClientTCPInterface.Close();
                }

                ClientTCPInterface = null;
                return;
            }
            finally
            {
                sw.Stop();
                if (Config.Debug.Enable && Config.Debug.MsgResponseTime) Log("Close " + sw.Elapsed.TotalMilliseconds + "ms");
            }
        }

        #endregion

        #region Private-Sync-Methods

        //
        // Ensure that none of these methods call another method within this region
        // otherwise you have a lock within a lock!  There should be NO methods
        // outside of this region that have a lock statement
        //

        private bool SyncResponseReady(string guid)
        {
            if (String.IsNullOrEmpty(guid))
            {
                Log("*** SyncResponseReady null GUID supplied");
                return false;
            }

            if (SyncResponses == null)
            {
                Log("*** SyncResponseReady null sync responses list, initializing");
                SyncResponses = new ConcurrentDictionary<string, Message>();
                return false;
            }

            if (SyncResponses.Count < 1)
            {
                Log("*** SyncResponseReady no entries in sync responses list");
                return false;
            }

            if (SyncResponses.ContainsKey(guid))
            {
                Log("SyncResponseReady found sync response for GUID " + guid);
                return true;
            }

            Log("*** SyncResponseReady no sync response for GUID " + guid);
            return false;
        }

        private bool AddSyncRequest(string guid)
        {
            if (String.IsNullOrEmpty(guid))
            {
                Log("*** AddSyncRequest null GUID supplied");
                return false;
            }

            if (SyncRequests == null)
            {
                Log("*** AddSyncRequest null sync requests list, initializing");
                SyncRequests = new ConcurrentDictionary<string, DateTime>();
            }

            if (SyncRequests.ContainsKey(guid))
            {
                Log("*** AddSyncRequest already contains an entry for GUID " + guid);
                return false;
            }

            SyncRequests.TryAdd(guid, DateTime.Now);
            Log("AddSyncRequest added request for GUID " + guid + ": " + DateTime.Now.ToString("MM/dd/yyyy hh:mm:ss"));
            return true;
        }

        private bool RemoveSyncRequest(string guid)
        {
            if (String.IsNullOrEmpty(guid))
            {
                Log("*** RemoveSyncRequest null GUID supplied");
                return false;
            }

            if (SyncRequests == null)
            {
                Log("*** RemoveSyncRequest null sync requests list, initializing");
                SyncRequests = new ConcurrentDictionary<string, DateTime>();
                return false;
            }

            DateTime TempDateTime;

            if (SyncRequests.ContainsKey(guid))
            {
                SyncRequests.TryRemove(guid, out TempDateTime);
            }

            Log("RemoveSyncRequest removed sync request for GUID " + guid);
            return true;
        }

        private bool SyncRequestExists(string guid)
        {
            if (String.IsNullOrEmpty(guid))
            {
                Log("*** SyncRequestExists null GUID supplied");
                return false;
            }
            
            if (SyncRequests == null)
            {
                Log("*** SyncRequestExists null sync requests list, initializing");
                SyncRequests = new ConcurrentDictionary<string, DateTime>();
                return false;
            }

            if (SyncRequests.Count < 1)
            {
                Log("*** SyncRequestExists empty sync requests list, returning false");
                return false;
            }

            if (SyncRequests.ContainsKey(guid))
            {
                Log("SyncRequestExists found sync request for GUID " + guid);
                return true;
            }

            Log("*** SyncRequestExists unable to find sync request for GUID " + guid);
            return false;
        }

        private bool AddSyncResponse(Message response)
        {
            if (response == null)
            {
                Log("*** AddSyncResponse null BigQMessage supplied");
                return false;
            }

            if (String.IsNullOrEmpty(response.MessageId))
            {
                Log("*** AddSyncResponse null MessageId within supplied message");
                return false;
            }
            
            if (SyncResponses.ContainsKey(response.MessageId))
            {
                Log("*** AddSyncResponse response already awaits for MessageId " + response.MessageId);
                return false;
            }

            SyncResponses.TryAdd(response.MessageId, response);
            Log("AddSyncResponse added sync response for MessageId " + response.MessageId);
            return true;
        }

        private bool GetSyncResponse(string guid, out Message response)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            response = new Message();
            DateTime start = DateTime.Now;
            bool timeoutExceeded = false;
            bool messageReceived = false;

            try
            {
                if (String.IsNullOrEmpty(guid))
                {
                    Log("*** GetSyncResponse null GUID supplied");
                    return false;
                }

                if (SyncResponses == null)
                {
                    Log("*** GetSyncResponse null sync responses list, initializing");
                    SyncResponses = new ConcurrentDictionary<string, Message>();
                    return false;
                }

                int iterations = 0;
                while (true)
                {
                    // if (Config.Debug.Enable && Config.Debug.MsgResponseTime) Log("GetSyncResponse iteration " + iterations + " " + sw.Elapsed.TotalMilliseconds + "ms");

                    if (SyncResponses.ContainsKey(guid))
                    {
                        if (!SyncResponses.TryGetValue(guid, out response))
                        {
                            Log("*** GetSyncResponse unable to retrieve sync response for GUID " + guid + " though one exists");
                            return false;
                        }

                        messageReceived = true;
                        Log("GetSyncResponse returning response for message GUID " + guid);
                        return true;
                    }

                    //
                    // Check if timeout exceeded
                    //
                    TimeSpan ts = DateTime.Now - start;
                    if (ts.TotalMilliseconds > Config.SyncTimeoutMs)
                    {
                        Log("*** GetSyncResponse timeout waiting for response for message GUID " + guid);
                        timeoutExceeded = true;
                        response = null;
                        return false;
                    }

                    iterations++;
                    continue;
                }
            }
            finally
            {
                if (messageReceived) Task.Run(() => RemoveSyncRequest(guid));

                sw.Stop();
                if (Config.Debug.Enable && Config.Debug.MsgResponseTime)
                {
                    Log("GetSyncResponse " + sw.Elapsed.TotalMilliseconds + "ms (timeout " + timeoutExceeded + ")");
                }
            }
        }
        
        private void CleanupSyncRequests()
        {
            while (true)
            {
                Thread.Sleep(Config.SyncTimeoutMs);
                List<string> ExpiredMessageIDs = new List<string>();
                DateTime TempDateTime;
                Message TempMessage;

                foreach (KeyValuePair<string, DateTime> CurrentRequest in SyncRequests)
                {
                    DateTime ExpirationDateTime = CurrentRequest.Value.AddMilliseconds(Config.SyncTimeoutMs);

                    if (DateTime.Compare(ExpirationDateTime, DateTime.Now) < 0)
                    {
                        #region Expiration-Earlier-Than-Current-Time

                        Log("*** CleanupSyncRequests adding MessageId " + CurrentRequest.Key + " (added " + CurrentRequest.Value.ToString("MM/dd/yyyy hh:mm:ss") + ") to cleanup list (past expiration time " + ExpirationDateTime.ToString("MM/dd/yyyy hh:mm:ss") + ")");
                        ExpiredMessageIDs.Add(CurrentRequest.Key);

                        #endregion
                    }
                }

                foreach (string CurrentRequestGuid in ExpiredMessageIDs)
                {
                    // 
                    // remove from sync requests
                    //
                    SyncRequests.TryRemove(CurrentRequestGuid, out TempDateTime);
                }
               
                foreach (string CurrentRequestGuid in ExpiredMessageIDs)
                {
                    if (SyncResponses.ContainsKey(CurrentRequestGuid))
                    {
                        //
                        // remove from sync responses
                        //
                        SyncResponses.TryRemove(CurrentRequestGuid, out TempMessage);
                    }
                }
              
            }
        }

        #endregion

        #region Private-Methods

        public bool ValidateCert(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            // return true; // Allow untrusted certificates.
            return Config.AcceptInvalidSSLCerts;
        }

        #region TCP-Server

        private bool TCPDataSender(Message Message)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            try
            {
                #region Check-for-Null-Values

                if (Message == null)
                {
                    Log("*** TCPDataSender null message supplied");
                    return false;
                }

                #endregion

                #region Check-if-Client-Connected

                if (!Helper.IsTCPPeerConnected(ClientTCPInterface))
                {
                    Log("TCPDataSender server " + ServerIP + ":" + ServerPort + " not connected");
                    Connected = false;

                    // 
                    //
                    // Do not fire event here; allow TCPDataReceiver to do it
                    //
                    //
                    // if (ServerDisconnected != null) ServerDisconnected();
                    return false;
                }

                #endregion

                #region Send-Message

                if (!Helper.TCPMessageWrite(ClientTCPInterface, Message, (Config.Debug.Enable && Config.Debug.MsgResponseTime)))
                {
                    Log("TCPDataSender unable to send data to server " + ServerIP + ":" + ServerPort);
                    return false;
                }
                else
                {
                    Log("TCPDataSender successfully sent message to server " + ServerIP + ":" + ServerPort);
                }

                #endregion

                return true;
            }
            finally
            {
                sw.Stop();
                if (Config.Debug.Enable && Config.Debug.MsgResponseTime) Log("TCPDataSender " + sw.Elapsed.TotalMilliseconds + "ms");
            }
        }
        
        private void TCPDataReceiver()
        {
            bool disconnectDetected = false;

            try
            {
                #region Attach-to-Stream

                if (!ClientTCPInterface.Connected)
                {
                    Log("*** TCPDataReceiver server " + ServerIP + ":" + ServerPort + " is no longer connected");
                    return;
                }

                NetworkStream ClientStream = ClientTCPInterface.GetStream();

                #endregion

                #region Wait-for-Data

                while (true)
                {
                    #region Check-if-Client-Connected-to-Server

                    if (!ClientTCPInterface.Connected || !Helper.IsTCPPeerConnected(ClientTCPInterface))
                    {
                        Log("*** TCPDataReceiver server " + ServerIP + ":" + ServerPort + " disconnected");
                        Connected = false;

                        disconnectDetected = true;
                        break;
                    }
                    else
                    {
                        // Log("TCPDataReceiver server " + ServerIP + ":" + ServerPort + " is still connected");
                    }

                    #endregion

                    #region Read-Message
                    
                    Message CurrentMessage = Helper.TCPMessageRead(ClientTCPInterface, (Config.Debug.Enable && (Config.Debug.Enable && Config.Debug.MsgResponseTime)));
                    if (CurrentMessage == null)
                    {
                        // Log("TCPDataReceiver unable to read message from server " + ServerIP + ":" + ServerPort);
                        Thread.Sleep(30);
                        continue;
                    }
                    else
                    {
                        /*
                        Console.WriteLine("");
                        Console.WriteLine(CurrentMessage.ToString());
                        Console.WriteLine("");
                        */
                    }

                    #endregion

                    #region Handle-Message

                    if (String.Compare(CurrentMessage.SenderGuid, "00000000-0000-0000-0000-000000000000") == 0
                        && !String.IsNullOrEmpty(CurrentMessage.Command)
                        && String.Compare(CurrentMessage.Command.ToLower().Trim(), "heartbeatrequest") == 0)
                    {
                        #region Handle-Incoming-Server-Heartbeat

                        // 
                        //
                        // do nothing, just continue
                        //
                        //

                        #endregion
                    }
                    else if (Helper.IsTrue(CurrentMessage.SyncRequest))
                    {
                        #region Handle-Incoming-Sync-Request

                        Log("TCPDataReceiver sync request detected for message GUID " + CurrentMessage.MessageId);

                        if (SyncMessageReceived != null)
                        {
                            byte[] ResponseData = SyncMessageReceived(CurrentMessage);

                            CurrentMessage.SyncRequest = false;
                            CurrentMessage.SyncResponse = true;
                            CurrentMessage.Data = ResponseData;

                            string TempGuid = String.Copy(CurrentMessage.SenderGuid);
                            CurrentMessage.SenderGuid = ClientGUID;
                            CurrentMessage.RecipientGuid = TempGuid;

                            TCPDataSender(CurrentMessage);
                            Log("TCPDataReceiver sent response message for message GUID " + CurrentMessage.MessageId);
                        }
                        else
                        {
                            Log("*** TCPDataReceiver sync request received for MessageId " + CurrentMessage.MessageId + " but no handler specified, sending async");
                            if (AsyncMessageReceived != null)
                            {
                                Task.Run(() => AsyncMessageReceived(CurrentMessage));
                            }
                            else
                            {
                                Log("*** TCPDataReceiver no method defined for AsyncMessageReceived");
                            }
                        }

                        #endregion
                    }
                    else if (Helper.IsTrue(CurrentMessage.SyncResponse))
                    {
                        #region Handle-Incoming-Sync-Response

                        Log("TCPDataReceiver sync response detected for message GUID " + CurrentMessage.MessageId);

                        if (SyncRequestExists(CurrentMessage.MessageId))
                        {
                            Log("TCPDataReceiver sync request exists for message GUID " + CurrentMessage.MessageId);

                            if (AddSyncResponse(CurrentMessage))
                            {
                                Log("TCPDataReceiver added sync response for message GUID " + CurrentMessage.MessageId);
                            }
                            else
                            {
                                Log("*** TCPDataReceiver unable to add sync response for MessageId " + CurrentMessage.MessageId + ", sending async");
                                if (AsyncMessageReceived != null)
                                {
                                    Task.Run(() => AsyncMessageReceived(CurrentMessage));
                                }
                                else
                                {
                                    Log("*** TCPDataReceiver no method defined for AsyncMessageReceived");
                                }

                            }
                        }
                        else
                        {
                            Log("*** TCPDataReceiver message marked as sync response but no sync request found for MessageId " + CurrentMessage.MessageId + ", sending async");
                            if (AsyncMessageReceived != null)
                            {
                                Task.Run(() => AsyncMessageReceived(CurrentMessage));
                            }
                            else
                            {
                                Log("*** TCPDataReceiver no method defined for AsyncMessageReceived");
                            }
                        }

                        #endregion
                    }
                    else
                    {
                        #region Handle-Async

                        Log("TCPDataReceiver async message GUID " + CurrentMessage.MessageId);

                        if (AsyncMessageReceived != null)
                        {
                            Task.Run(() => AsyncMessageReceived(CurrentMessage));
                        }
                        else
                        {
                            Log("*** TCPDataReceiver no method defined for AsyncMessageReceived");
                        }

                        #endregion
                    }

                    #endregion
                }

                #endregion
            }
            catch (ObjectDisposedException)
            {
                Log("*** TCPDataReceiver no longer connected (object disposed exception)");
            }
            catch (Exception EOuter)
            {
                Log("*** TCPDataReceiver outer exception detected");
                LogException("TCPDataReceiver", EOuter);
            }
            finally
            {
                if (disconnectDetected || !Connected)
                {
                    if (ServerDisconnected != null)
                    {
                        Task.Run(() => ServerDisconnected());
                    }
                }
            }
        }

        #endregion

        #region TCP-SSL-Server

        private bool TCPSSLDataSender(Message Message)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            try
            {
                #region Check-for-Null-Values

                if (Message == null)
                {
                    Log("*** TCPSSLDataSender null message supplied");
                    return false;
                }

                #endregion

                #region Check-if-Client-Connected

                if (!Helper.IsTCPPeerConnected(ClientTCPSSLInterface))
                {
                    Log("TCPSSLDataSender server " + ServerIP + ":" + ServerPort + " not connected");
                    Connected = false;

                    // 
                    //
                    // Do not fire event here; allow TCPDataReceiver to do it
                    //
                    //
                    // if (ServerDisconnected != null) ServerDisconnected();
                    return false;
                }

                #endregion

                #region Send-Message

                if (!Helper.TCPSSLMessageWrite(ClientTCPSSLInterface, ClientSSLStream, Message, (Config.Debug.Enable && Config.Debug.MsgResponseTime)))
                {
                    Log("TCPSSLDataSender unable to send data to server " + ServerIP + ":" + ServerPort);
                    return false;
                }
                else
                {
                    Log("TCPSSLDataSender successfully sent message to server " + ServerIP + ":" + ServerPort);
                }

                #endregion

                return true;
            }
            finally
            {
                sw.Stop();
                if (Config.Debug.Enable && Config.Debug.MsgResponseTime) Log("TCPSSLDataSender " + sw.Elapsed.TotalMilliseconds + "ms");
            }
        }

        private void TCPSSLDataReceiver()
        {
            bool disconnectDetected = false;

            try
            {
                #region Attach-to-Stream

                if (!ClientTCPSSLInterface.Connected)
                {
                    Log("*** TCPSSLDataReceiver server " + ServerIP + ":" + ServerPort + " is no longer connected");
                    return;
                }
                
                #endregion

                #region Wait-for-Data

                while (true)
                {
                    #region Check-if-Client-Connected-to-Server

                    if (!ClientTCPSSLInterface.Connected || !Helper.IsTCPPeerConnected(ClientTCPSSLInterface))
                    {
                        Log("*** TCPSSLDataReceiver server " + ServerIP + ":" + ServerPort + " disconnected");
                        Connected = false;

                        disconnectDetected = true;
                        break;
                    }
                    else
                    {
                        // Log("TCPSSLDataReceiver server " + ServerIP + ":" + ServerPort + " is still connected");
                    }

                    #endregion

                    #region Read-Message

                    Message CurrentMessage = Helper.TCPSSLMessageRead(ClientTCPSSLInterface, ClientSSLStream, (Config.Debug.Enable && (Config.Debug.Enable && Config.Debug.MsgResponseTime)));
                    if (CurrentMessage == null)
                    {
                        // Log("TCPSSLDataReceiver unable to read message from server " + ServerIP + ":" + ServerPort);
                        Thread.Sleep(30);
                        continue;
                    }
                    else
                    {
                        /*
                        Console.WriteLine("");
                        Console.WriteLine(CurrentMessage.ToString());
                        Console.WriteLine("");
                        */
                    }

                    #endregion

                    #region Handle-Message

                    if (String.Compare(CurrentMessage.SenderGuid, "00000000-0000-0000-0000-000000000000") == 0
                        && !String.IsNullOrEmpty(CurrentMessage.Command)
                        && String.Compare(CurrentMessage.Command.ToLower().Trim(), "heartbeatrequest") == 0)
                    {
                        #region Handle-Incoming-Server-Heartbeat

                        // 
                        //
                        // do nothing, just continue
                        //
                        //

                        #endregion
                    }
                    else if (Helper.IsTrue(CurrentMessage.SyncRequest))
                    {
                        #region Handle-Incoming-Sync-Request

                        Log("TCPSSLDataReceiver sync request detected for message GUID " + CurrentMessage.MessageId);

                        if (SyncMessageReceived != null)
                        {
                            byte[] ResponseData = SyncMessageReceived(CurrentMessage);

                            CurrentMessage.SyncRequest = false;
                            CurrentMessage.SyncResponse = true;
                            CurrentMessage.Data = ResponseData;

                            string TempGuid = String.Copy(CurrentMessage.SenderGuid);
                            CurrentMessage.SenderGuid = ClientGUID;
                            CurrentMessage.RecipientGuid = TempGuid;

                            TCPSSLDataSender(CurrentMessage);
                            Log("TCPSSLDataReceiver sent response message for message GUID " + CurrentMessage.MessageId);
                        }
                        else
                        {
                            Log("*** TCPSSLDataReceiver sync request received for MessageId " + CurrentMessage.MessageId + " but no handler specified, sending async");
                            if (AsyncMessageReceived != null)
                            {
                                Task.Run(() => AsyncMessageReceived(CurrentMessage));
                            }
                            else
                            {
                                Log("*** TCPSSLDataReceiver no method defined for AsyncMessageReceived");
                            }
                        }

                        #endregion
                    }
                    else if (Helper.IsTrue(CurrentMessage.SyncResponse))
                    {
                        #region Handle-Incoming-Sync-Response

                        Log("TCPSSLDataReceiver sync response detected for message GUID " + CurrentMessage.MessageId);

                        if (SyncRequestExists(CurrentMessage.MessageId))
                        {
                            Log("TCPSSLDataReceiver sync request exists for message GUID " + CurrentMessage.MessageId);

                            if (AddSyncResponse(CurrentMessage))
                            {
                                Log("TCPSSLDataReceiver added sync response for message GUID " + CurrentMessage.MessageId);
                            }
                            else
                            {
                                Log("*** TCPSSLDataReceiver unable to add sync response for MessageId " + CurrentMessage.MessageId + ", sending async");
                                if (AsyncMessageReceived != null)
                                {
                                    Task.Run(() => AsyncMessageReceived(CurrentMessage));
                                }
                                else
                                {
                                    Log("*** TCPSSLDataReceiver no method defined for AsyncMessageReceived");
                                }

                            }
                        }
                        else
                        {
                            Log("*** TCPSSLDataReceiver message marked as sync response but no sync request found for MessageId " + CurrentMessage.MessageId + ", sending async");
                            if (AsyncMessageReceived != null)
                            {
                                Task.Run(() => AsyncMessageReceived(CurrentMessage));
                            }
                            else
                            {
                                Log("*** TCPSSLDataReceiver no method defined for AsyncMessageReceived");
                            }
                        }

                        #endregion
                    }
                    else
                    {
                        #region Handle-Async

                        Log("TCPSSLDataReceiver async message GUID " + CurrentMessage.MessageId);

                        if (AsyncMessageReceived != null)
                        {
                            Task.Run(() => AsyncMessageReceived(CurrentMessage));
                        }
                        else
                        {
                            Log("*** TCPSSLDataReceiver no method defined for AsyncMessageReceived");
                        }

                        #endregion
                    }

                    #endregion
                }

                #endregion
            }
            catch (ObjectDisposedException)
            {
                Log("*** TCPSSLDataReceiver no longer connected (object disposed exception)");
            }
            catch (Exception EOuter)
            {
                Log("*** TCPSSLDataReceiver outer exception detected");
                LogException("TCPSSLDataReceiver", EOuter);
            }
            finally
            {
                if (disconnectDetected || !Connected)
                {
                    if (ServerDisconnected != null)
                    {
                        Task.Run(() => ServerDisconnected());
                    }
                }
            }
        }

        #endregion
        
        private void HeartbeatManager()
        {
            try
            {
                #region Check-for-Disable

                if (!Config.Heartbeat.Enable)
                {
                    Log("HeartbeatManager disabled");
                    return;
                }

                if (Config.Heartbeat.IntervalMs == 0)
                {
                    Log("HeartbeatManager disabled");
                    return;
                }

                #endregion

                #region Check-for-Null-Values

                if (Config.TcpServer.Enable)
                {
                    if (ClientTCPInterface == null)
                    {
                        Log("*** HeartbeatManager null TCP client supplied");
                        return;
                    }
                }
                else if (Config.TcpSSLServer.Enable)
                {
                    if (ClientTCPSSLInterface == null)
                    {
                        Log("*** HeartbeatManager null TCP SSL client supplied");
                        return;
                    }
                }

                #endregion

                #region Variables

                DateTime threadStart = DateTime.Now;
                DateTime lastHeartbeatAttempt = DateTime.Now;
                DateTime lastSuccess = DateTime.Now;
                DateTime lastFailure = DateTime.Now;
                int numConsecutiveFailures = 0;
                bool firstRun = true;

                #endregion

                #region Process

                while (true)
                {
                    #region Sleep

                    if (firstRun)
                    {
                        firstRun = false;
                    }
                    else
                    {
                        Thread.Sleep(Config.Heartbeat.IntervalMs);
                    }

                    #endregion

                    #region Check-if-Client-Connected

                    if (Config.TcpServer.Enable)
                    {
                        if (!Helper.IsTCPPeerConnected(ClientTCPInterface))
                        {
                            Log("HeartbeatManager TCP client disconnected from server " + ServerIP + ":" + ServerPort);
                            Connected = false;
                            return;
                        }
                    }
                    else if (Config.TcpServer.Enable)
                    {
                        if (!Helper.IsTCPPeerConnected(ClientTCPSSLInterface))
                        {
                            Log("HeartbeatManager TCP SSL client disconnected from server " + ServerIP + ":" + ServerPort);
                            Connected = false;
                            return;
                        }
                    }

                    #endregion

                    #region Send-Heartbeat-Message

                    lastHeartbeatAttempt = DateTime.Now;
                    Message HeartbeatMessage = HeartbeatRequestMessage();
                    
                    if (!SendServerMessageAsync(HeartbeatMessage))
                    { 
                        numConsecutiveFailures++;
                        lastFailure = DateTime.Now;

                        Log("*** HeartbeatManager failed to send heartbeat to server " + ServerIP + ":" + ServerPort + " (" + numConsecutiveFailures + "/" + Config.Heartbeat.MaxFailures + " consecutive failures)");

                        if (numConsecutiveFailures >= Config.Heartbeat.MaxFailures)
                        {
                            Log("*** HeartbeatManager maximum number of failed heartbeats reached");
                            Connected = false;
                            return;
                        }
                    }
                    else
                    {
                        numConsecutiveFailures = 0;
                        lastSuccess = DateTime.Now;
                    }

                    #endregion
                }

                #endregion
            }
            catch (Exception EOuter)
            {
                LogException("HeartbeatManager", EOuter);
            }
            finally
            {
            }
        }

        private Message HeartbeatRequestMessage()
        {
            Message ResponseMessage = new Message();
            ResponseMessage.MessageId = Guid.NewGuid().ToString();
            ResponseMessage.RecipientGuid = "00000000-0000-0000-0000-000000000000";
            ResponseMessage.Command = "HeartbeatRequest";
            ResponseMessage.SenderGuid = ClientGUID;
            ResponseMessage.CreatedUTC = DateTime.Now.ToUniversalTime();
            ResponseMessage.Data = null;
            return ResponseMessage;
        }

        #endregion

        #region Private-Utility-Methods

        private void Log(string message)
        {
            if (LogMessage != null) LogMessage(message);
            if (Config.Debug.Enable && Config.Debug.ConsoleLogging)
            {
                Console.WriteLine(message);
            }
        }

        private void LogException(string method, Exception e)
        {
            Log("================================================================================");
            Log(" = Method: " + method);
            Log(" = Exception Type: " + e.GetType().ToString());
            Log(" = Exception Data: " + e.Data);
            Log(" = Inner Exception: " + e.InnerException);
            Log(" = Exception Message: " + e.Message);
            Log(" = Exception Source: " + e.Source);
            Log(" = Exception StackTrace: " + e.StackTrace);
            Log("================================================================================");
        }

        #endregion
    }
}
