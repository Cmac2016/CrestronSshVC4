using FxSsh.Messages;
using FxSsh.Messages.Connection;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace FxSsh.Services
{
    public class ConnectionService : SshService
    {
        private readonly object _locker = new object();
        private readonly List<Channel> _channels = new List<Channel>();
        private readonly UserauthArgs _auth;

        private int _serverChannelCounter = -1;

        public ConnectionService(Session session, UserauthArgs auth)
            : base(session)
        {
            Contract.Requires(auth != null);

            _auth = auth;
        }

        public event EventHandler<SessionRequestedArgs> CommandOpened;
        public event EventHandler<DirectTcpIpRequestedArgs> DirectTcpIpReceived;

     
        protected internal override void CloseService()
        {
            lock (_locker)
                foreach (var channel in _channels.ToArray())
                {
                    channel.ForceClose();
                }
        }

        internal void HandleMessageCore(ConnectionServiceMessage message)
        {
            Contract.Requires(message != null);

            typeof(ConnectionService)
                .GetMethod("HandleMessage", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { message.GetType() }, null)
                ?.Invoke(this, new object[] { message });
        }

        private void HandleMessage(ChannelOpenMessage message)
        {
            switch (message.ChannelType)
            {
                case "session":
                    var msg = Message.LoadFrom<SessionOpenMessage>(message);
                    HandleMessage(msg);
                    break;
                case "direct-tcpip":
                    var msg2 = Message.LoadFrom<DirectTcpIpMessage>(message);
                    //HandleMessage(msg2);
                    break;
                default:
                    _session.SendMessage(new ChannelOpenFailureMessage
                    {
                        RecipientChannel = message.SenderChannel,
                        ReasonCode = ChannelOpenFailureReason.UnknownChannelType,
                        Description = $"Unknown channel type: {message.ChannelType}.",
                    });
                    throw new SshConnectionException($"Unknown channel type: {message.ChannelType}.");
            }
        }

        private void HandleMessage(ChannelRequestMessage message)
        {
            switch (message.RequestType)
            {
                case "exec":
                    var msg = Message.LoadFrom<CommandRequestMessage>(message);
                    HandleMessage(msg);
                    break;
                case "pty-req":
                    var msg2 = Message.LoadFrom<PTYRequestMessage>(message);
                    HandleMessage(msg2);
                    break;
                case "subsystem":
                    var msg3 = Message.LoadFrom<SubsystemRequestMessage>(message);
                    HandleMessage(msg3);
                    break;
                case "shell":
                    var msg4 = Message.LoadFrom<ShellRequestMessage>(message);
                    HandleMessage(msg4);
                    break;
                default:
                    if (message.WantReply)
                        _session.SendMessage(new ChannelFailureMessage
                        {
                            RecipientChannel = FindChannelByServerId<Channel>(message.RecipientChannel).ClientChannelId
                        });
                    throw new SshConnectionException($"Unknown request type: {message.RequestType}.");
            }
        }

        private void HandleMessage(ChannelDataMessage message)
        {
            var channel = FindChannelByServerId<Channel>(message.RecipientChannel);
            channel.OnData(message.Data);
        }

        private bool _echo = true;
        private StringBuilder _stringBuilder;
        private void HandleMessage(ShellRequestMessage message)
        {
         
            var channel = FindChannelByServerId<Channel>(message.RecipientChannel);
            ConsoleData._channel = channel;
            if (_stringBuilder is null)
                _stringBuilder = new StringBuilder();
            channel.DataReceived += (sender, bytes) =>
            {
                //if (sender != _session) return;
                if (bytes[0] == 27 && bytes[1] == 91 && bytes[2] == 66)
                {
                    //this seems to be a putty ping pong type message
                    channel.SendData(new byte[] { 27,91,66 });
                    return;
                }

                if (bytes[bytes.Length - 1] != 13)
                {
                    var data = System.Text.Encoding.UTF8
                        .GetString(bytes); //.Replace("\n",  string.Empty).Replace("\r", string.Empty);
                    if (_echo) channel.SendData(bytes);

                    _stringBuilder.Append(data);

                }

                if (bytes[bytes.Length - 1] == 13)
                {
                    if (_stringBuilder.Length > 2)
                    {
                        Console.WriteLine("Data : " + _stringBuilder.ToString());
                        Console.WriteLine("Data length : " + _stringBuilder.ToString().Length);
                        ConsoleData.DataFromConsole(_stringBuilder.ToString());
                        _stringBuilder.Clear();
                       // channel.SendData(System.Text.Encoding.UTF8.GetBytes("\r\nVC-4>"));
                    }
                    channel.SendData(System.Text.Encoding.UTF8.GetBytes("\r\nVC-4>"));
                }
            };
            if (message.WantReply)
                _session.SendMessage(new ChannelSuccessMessage { RecipientChannel = channel.ClientChannelId });
            
            channel.SendData(System.Text.Encoding.UTF8.GetBytes("VC-4 Control Console\r\nVC-4>"));
        }

        private void ConsoleData_OnDataReceived(object sender, DataChangedEventsArgs e)
        {
            throw new NotImplementedException();
        }

        private void HandleMessage(ChannelWindowAdjustMessage message)
        {
            var channel = FindChannelByServerId<Channel>(message.RecipientChannel);
            channel.ClientAdjustWindow(message.BytesToAdd);
        }

        private void HandleMessage(ChannelEofMessage message)
        {
            var channel = FindChannelByServerId<Channel>(message.RecipientChannel);
            channel.OnEof();
        }

        private void HandleMessage(ChannelCloseMessage message)
        {
            var channel = FindChannelByServerId<Channel>(message.RecipientChannel);
            channel.OnClose();
        }

        private void HandleMessage(DirectTcpIpMessage message)
        {
            var channel = new DirectTcpIpSessionChannel(
                this, message,
                (uint)Interlocked.Increment(ref _serverChannelCounter));

            if (DirectTcpIpReceived != null)
            {
                var args = new DirectTcpIpRequestedArgs(channel, message.HostToConnect, message.PortToConnect, message.OriginatorIPAddress, message.OriginatorPort, _auth);
                DirectTcpIpReceived(this, args);

                if (!args.Allow)
                {
                    var fMsg = new ChannelOpenFailureMessage
                    {
                        RecipientChannel = channel.ClientChannelId,
                        Description = args.DenialDescription ?? "not specified",
                        ReasonCode = args.ReasonCode
                    };
                    _session.SendMessage(fMsg);
                    return;
                }
            }

            try
            {
                channel.ConnectToTarget();
            }
            catch (Exception e)
            {
                var fMsg = new ChannelOpenFailureMessage
                {
                    RecipientChannel = channel.ClientChannelId,
                    Description = e.Message,
                    ReasonCode = ChannelOpenFailureReason.ConnectFailed
                };
                _session.SendMessage(fMsg);
                return;
            }

            lock (_locker)
                _channels.Add(channel);

            var msg = new SessionOpenConfirmationMessage
            {
                RecipientChannel = channel.ClientChannelId,
                SenderChannel = channel.ServerChannelId,
                InitialWindowSize = channel.ServerInitialWindowSize,
                MaximumPacketSize = channel.ServerMaxPacketSize
            };

            _session.SendMessage(msg);
        }

        private void HandleMessage(SessionOpenMessage message)
        {
            var channel = new SessionChannel(
                this,
                message.SenderChannel,
                message.InitialWindowSize,
                message.MaximumPacketSize,
                (uint)Interlocked.Increment(ref _serverChannelCounter));

            lock (_locker)
                _channels.Add(channel);

            var msg = new SessionOpenConfirmationMessage
            {
                RecipientChannel = channel.ClientChannelId,
                SenderChannel = channel.ServerChannelId,
                InitialWindowSize = channel.ServerInitialWindowSize,
                MaximumPacketSize = channel.ServerMaxPacketSize
            };

            _session.SendMessage(msg);
        }

        private void HandleMessage(PTYRequestMessage message)
        {
            var channel = FindChannelByServerId<SessionChannel>(message.RecipientChannel);

            if (message.WantReply)
                _session.SendMessage(new ChannelSuccessMessage { RecipientChannel = channel.ClientChannelId });
        }

        private void HandleMessage(SubsystemRequestMessage message)
        {
            var channel = FindChannelByServerId<SessionChannel>(message.RecipientChannel);

            if (message.WantReply)
                _session.SendMessage(new ChannelSuccessMessage { RecipientChannel = channel.ClientChannelId });

            if (CommandOpened != null)
            {
                var args = new SessionRequestedArgs(channel, message.SubsystemName, "open", _auth);
                CommandOpened(this, args);
            }
        }

        private void HandleMessage(CommandRequestMessage message)
        {
            var channel = FindChannelByServerId<SessionChannel>(message.RecipientChannel);

            if (message.WantReply)
                _session.SendMessage(new ChannelSuccessMessage { RecipientChannel = channel.ClientChannelId });

            if (CommandOpened == null) return;
            var args = new SessionRequestedArgs(channel, null, message.Command, _auth);
            CommandOpened(this, args);
        }

        private T FindChannelByClientId<T>(uint id) where T : Channel
        {
            lock (_locker)
            {
                var channel = _channels.FirstOrDefault(x => x.ClientChannelId == id) as T;
                if (channel == null)
                    throw new SshConnectionException($"Invalid client channel id {id}.",
                        DisconnectReason.ProtocolError);

                return channel;
            }
        }

        private T FindChannelByServerId<T>(uint id) where T : Channel
        {
            lock (_locker)
            {
                var channel = _channels.FirstOrDefault(x => x.ServerChannelId == id) as T;
                if (channel == null)
                    throw new SshConnectionException($"Invalid server channel id {id}.",
                        DisconnectReason.ProtocolError);

                return channel;
            }
        }

        internal void RemoveChannel(Channel channel)
        {
            lock (_locker)
            {
                _channels.Remove(channel);
            }
        }
    }

  
}
