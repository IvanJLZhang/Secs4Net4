#region 文件说明
/*------------------------------------------------------------------------------
// Copyright © 2018 Granda. All Rights Reserved.
// 苏州广林达电子科技有限公司 版权所有
//------------------------------------------------------------------------------
// File Name: SecsGem
// Author: Ivan JL Zhang    Date: 2018/4/25 15:27:13    Version: 1.0.0
// Description: 
//   
// 
// Revision History: 
// <Author>  		<Date>     	 	<Revision>  		<Modification>
// 	
//----------------------------------------------------------------------------*/
#endregion
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AsyncTCP;
using Secs4Frmk4.Properties;

namespace Secs4Frmk4
{
    public class SecsGem : IDisposable
    {
        #region event
        /// <summary>
        /// HSMS connection sate changed event
        /// </summary>
        public event EventHandler<TEventArgs<ConnectionState>> ConnectionChanged;

        /// <summary>
        /// Primary message received event
        /// </summary>
        public event EventHandler<PrimaryMessageWrapper> PrimaryMessageReceived = DefaultPrimaryMessageReceived;
        private static void DefaultPrimaryMessageReceived(object sender, PrimaryMessageWrapper _) { }
        #endregion

        #region Properties
        /// <summary>
        /// Connection state
        /// </summary>
        public ConnectionState State { get; private set; }

        /// <summary>
        /// Device Id.
        /// </summary>
        public ushort DeviceId { get; set; } = 0;

        /// <summary>
        /// T3 timer interval 
        /// <para/>Reply timeout
        /// </summary>
        public int T3 { get; set; } = 40000;

        /// <summary>
        /// T5 timer interval
        /// <para/>Connect Separation timeout
        /// </summary>
        public int T5 { get; set; } = 5000;

        /// <summary>
        /// T6 timer interval
        /// <para/>Control Timeout
        /// </summary>
        public int T6 { get; set; } = 5000;

        /// <summary>
        /// T7 timer interval
        /// <para/>Connection Idle Timeout
        /// </summary>
        public int T7 { get; set; } = 10000;

        /// <summary>
        /// T8 timer interval
        /// <para/>network intercharacter timeout
        /// </summary>
        public int T8 { get; set; } = 5000;

        public bool IsActive { get; }
        public IPAddress IpAddress { get; }
        public int Port { get; }
        #endregion

        #region field
        private readonly ConcurrentDictionary<int, TaskCompletionSourceToken> _replyExpectedMsgs = new ConcurrentDictionary<int, TaskCompletionSourceToken>();

        private readonly Timer _timer7;	// between socket connected and received Select.req timer
        private readonly Timer _timer8;
        private readonly Timer _timerLinkTest;

        private readonly Action _startImpl;
        private readonly Action _stopImpl;

        private readonly SystemByteGenerator _systemByte = new SystemByteGenerator();
        internal int NewSystemId => _systemByte.New();



        private readonly TaskFactory _taskFactory = new TaskFactory(TaskScheduler.Default);

        AsyncTcpClient _tcpClient;
        AsyncTcpServer tcpServer;

        #endregion

        #region ctor and start/stop methods
        public SecsGem(bool isActive, IPAddress ip, int port)
        {
            IsActive = isActive;
            IpAddress = ip;
            Port = port;
            _secsDecoder = new StreamDecoder(HandlerControlMessage, HandleDataMessage);

            #region Timer Action
            _timer7 = new Timer(delegate
            {
                Logger.Error($"T7 Timeout: {T7 / 1000} sec.");
                CommunicationStateChanging(ConnectionState.Retry);
            }, null, Timeout.Infinite, Timeout.Infinite);

            _timer8 = new Timer(delegate
            {
                Logger.Error($"T8 Timeout: {T8 / 1000} sec.");
                CommunicationStateChanging(ConnectionState.Retry);
            }, null, Timeout.Infinite, Timeout.Infinite);

            _timerLinkTest = new Timer(delegate
            {
                if (State == ConnectionState.Selected)
                    SendControlMessage(MessageType.LinkTestRequest, NewSystemId);
            }, null, Timeout.Infinite, Timeout.Infinite);

            #endregion

            _startImpl = () =>
            {
                if (IsActive)
                {
                    _tcpClient = new AsyncTcpClient(IpAddress, Port);
                    _tcpClient.ServerConnected += AsyncTcpClient_ServerConnected;
                    _tcpClient.ServerDisconnected += AsyncTcpClient_ServerDisconnected;
                    _tcpClient.ServerExceptionOccurred += TcpClient_ServerExceptionOccurred;
                    _tcpClient.DatagramReceived += DatagramReceived;
                    _tcpClient.RetryInterval = T5 / 1000;
                    CommunicationStateChanging(ConnectionState.Connecting);
                    _tcpClient.Connect();
                }
                else
                {
                    tcpServer = new AsyncTcpServer(Port);
                    tcpServer.ClientConnected += AsyncTcpServer_ClientConnected;
                    tcpServer.ClientDisconnected += AsyncTcpServer_ClientDisconnected;
                    tcpServer.DatagramReceived += DatagramReceived;
                    CommunicationStateChanging(ConnectionState.Connecting);
                    tcpServer.Start();
                }
            };
            _stopImpl = () =>
            {
                if (IsActive)
                {
                    _tcpClient.Close();
                    _tcpClient.ServerConnected -= AsyncTcpClient_ServerConnected;
                    _tcpClient.ServerDisconnected -= AsyncTcpClient_ServerDisconnected;
                    _tcpClient.ServerExceptionOccurred -= TcpClient_ServerExceptionOccurred;
                    _tcpClient.DatagramReceived -= DatagramReceived;
                }
                else
                {
                    tcpServer.Stop();
                    tcpServer.ClientConnected -= AsyncTcpServer_ClientConnected;
                    tcpServer.ClientDisconnected -= AsyncTcpServer_ClientDisconnected;
                    tcpServer.DatagramReceived -= DatagramReceived;
                }
            };
        }

        public void Start() => new TaskFactory(TaskScheduler.Default).StartNew(_startImpl);
        //public void Start() => _startImpl();

        public void Stop() => new TaskFactory(TaskScheduler.Default).StartNew(_stopImpl);

        private void Reset()
        {
            _timer7.Change(Timeout.Infinite, Timeout.Infinite);
            _timer8.Change(Timeout.Infinite, Timeout.Infinite);
            _timerLinkTest.Change(Timeout.Infinite, Timeout.Infinite);

            _secsDecoder.Reset();
            _replyExpectedMsgs.Clear();
            _stopImpl();
        }

        #endregion

        #region connection
        private void AsyncTcpServer_ClientDisconnected(object sender, TcpClientDisConnectedEventArgs e)
        {

        }

        private void AsyncTcpClient_ServerDisconnected(object sender, TcpServerDisconnectedEventArgs e)
        {

        }

        private void AsyncTcpServer_ClientConnected(object sender, TcpClientConnectedEventArgs e)
        {
            CommunicationStateChanging(ConnectionState.Connected);
        }

        private void AsyncTcpClient_ServerConnected(object sender, TcpServerConnectedEventArgs e)
        {
            CommunicationStateChanging(ConnectionState.Connected);
            SendControlMessage(MessageType.SelectRequest, NewSystemId);
        }
        private void TcpClient_ServerExceptionOccurred(object sender, TcpServerExceptionOccurredEventArgs e)
        {
            CommunicationStateChanging(ConnectionState.Retry);
        }

        private void CommunicationStateChanging(ConnectionState state)
        {
            State = state;
            ConnectionChanged?.Invoke(this, new TEventArgs<ConnectionState>(state));

            switch (State)
            {
                case ConnectionState.Connecting:
                    break;
                case ConnectionState.Connected:
#if !DISABLE_TIMER
                    Logger.Info($"Start T7 Timer: {T7 / 1000} sec.");
                    _timer7.Change(T7, Timeout.Infinite);
#endif
                    break;
                case ConnectionState.Selected:
                    _timer7.Change(Timeout.Infinite, Timeout.Infinite);
                    Logger.Info("Stop T7 Timer");
                    break;
                case ConnectionState.Retry:
                    if (IsDisposed)
                        return;
                    Reset();

                    Start();
                    break;
                default:
                    break;
            }
        }
        #endregion

        #region receive
        private void DatagramReceived(object sender, TcpDatagramReceivedEventArgs<byte[]> e)
        {
            try
            {
                _timer8.Change(Timeout.Infinite, Timeout.Infinite);
                var receivedCount = e.ReceivedCount;
                if (receivedCount == 0)
                {
                    Logger.Error("Received 0 byte.");
                    CommunicationStateChanging(ConnectionState.Retry);
                    return;
                }
                _secsDecoder.Buffer = e.ReceivedBytes;
                if (_secsDecoder.Decode(receivedCount))
                {// 尚未接收完全，开启T8
#if !DISABLE_T8
                    Logger.Info($"Start T8 Timer: {T8 / 1000} sec.");
                    _timer8.Change(T8, Timeout.Infinite);
#endif
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Unexpected exception", ex);
                CommunicationStateChanging(ConnectionState.Retry);
            }
        }

        private void HandleDataMessage(MessageHeader header, SecsMessage secsMessage)
        {
            var systemByte = header.SystemBytes;
            if (header.DeviceId != DeviceId && secsMessage.S != 9 && secsMessage.F != 1)
            {
                Logger.Error("Received Unrecognized Device Id Message");
                SendDataMessageAsync(new SecsMessage(
                    9,
                    1,
                    false,
                    "Unrecognized Device Id",
                    Item.B(header.EncodeTo(new byte[10]))),// 将header作为消息体返回
                    NewSystemId);
                return;
            }

            if (secsMessage.F % 2 != 0)
            {
                if (secsMessage.S != 9)
                {
                    _taskFactory.StartNew((wrapper) =>
                    {
                        PrimaryMessageReceived?.Invoke(this, wrapper as PrimaryMessageWrapper);
                    }, new PrimaryMessageWrapper(this, header, secsMessage));
                    return;
                }
                // Error message
                var headerBytes = secsMessage.SecsItem.GetValues<byte>();// 解析出MessageHeader的Bytes
                systemByte = BitConverter.ToInt32(new byte[] { headerBytes[9], headerBytes[8], headerBytes[7], headerBytes[6] }, 0);
            }

            if (_replyExpectedMsgs.TryGetValue(systemByte, out var ar))
                ar.HandleReplyMessage(secsMessage);
        }

        private void HandlerControlMessage(MessageHeader header)
        {
            var systemByte = header.SystemBytes;
            if ((byte)header.MessageType % 2 == 0)
            {// 收到Control message的response信息
                if (_replyExpectedMsgs.TryGetValue(systemByte, out var ar))
                {
                    ar.SetResult(ControlMessage);
                }
                else
                {
                    Logger.Error("Received Unexpected Control Message: " + header.MessageType);
                    return;
                }
            }

            Logger.Info("Received Control Message: " + header.MessageType);
            switch (header.MessageType)
            {
                case MessageType.DataMessage:
                    break;
                case MessageType.SelectRequest:
                    SendControlMessage(MessageType.SelectResponse, systemByte);
                    break;
                case MessageType.SelectResponse:
                    switch (header.F)
                    {
                        case 0:
                            CommunicationStateChanging(ConnectionState.Selected);
                            break;
                        case 1:
                            Logger.Error("Communication Already Active.");
                            break;
                        case 2:
                            Logger.Error("Connection Not Ready");
                            break;
                        case 3:
                            Logger.Error("Connection Exhaust");
                            break;
                        default:
                            Logger.Error("Connection Status is unknown.");
                            break;
                    }
                    break;
                case MessageType.Deselect_req:
                    break;
                case MessageType.Deselect_rsp:
                    break;
                case MessageType.LinkTestRequest:
                    SendControlMessage(MessageType.LinkTestResponse, systemByte);
                    break;
                case MessageType.LinkTestResponse:
                    break;
                case MessageType.Reject_req:
                    break;
                case MessageType.SeperateRequest:
                    CommunicationStateChanging(ConnectionState.Retry);
                    break;
                default:
                    break;
            }
        }
        #endregion

        #region send message
        private void SendControlMessage(MessageType messageType, int systemByte)
        {
            var token = new TaskCompletionSourceToken(ControlMessage, systemByte, messageType);
            if ((byte)messageType % 2 == 1 && messageType != MessageType.SeperateRequest)
            {
                _replyExpectedMsgs[systemByte] = token;
            }

            var bufferList = new List<ArraySegment<byte>>(2)
            {
                ControlMessageLengthBytes,
                new ArraySegment<byte>(new MessageHeader{
                    DeviceId = 0xFFFF,
                    MessageType = messageType,
                    SystemBytes = systemByte
                }.EncodeTo(new byte[10]))
            };

            if (IsActive)
            {
                _tcpClient.Send(bufferList);
                SendDataMessageCompleteHandler(_tcpClient, token);
            }
            else
            {
                tcpServer.SyncSendToAll(bufferList);
                SendDataMessageCompleteHandler(tcpServer, token);
            }
        }

        private void SendControlMessageHandler(object sender, TaskCompletionSourceToken e)
        {
            Logger.Info("Sent Control Message: " + e.MsgType);
            if (_replyExpectedMsgs.ContainsKey(e.Id))
            {
                if (!e.Task.Wait(T6))
                {
                    Logger.Error($"T6 Timeout: {T6 / 1000} sec.");
                    CommunicationStateChanging(ConnectionState.Retry);
                }
                _replyExpectedMsgs.TryRemove(e.Id, out _);
            }
        }

        internal Task<SecsMessage> SendDataMessageAsync(SecsMessage secsMessage, int systemByte)
        {
            if (State != ConnectionState.Selected)
                throw new SecsException("Device is not selected");

            var token = new TaskCompletionSourceToken(secsMessage, systemByte, MessageType.DataMessage);

            if (secsMessage.ReplyExpected)
                _replyExpectedMsgs[systemByte] = token;

            var header = new MessageHeader()
            {
                S = secsMessage.S,
                F = secsMessage.F,
                ReplyExpected = secsMessage.ReplyExpected,
                DeviceId = DeviceId,
                SystemBytes = systemByte,
            };
            var bufferList = secsMessage.RawDatas.Value;
            bufferList[1] = new ArraySegment<byte>(header.EncodeTo(new byte[10]));
            if (IsActive)
            {
                _tcpClient.Send(bufferList);
                SendDataMessageCompleteHandler(_tcpClient, token);
            }
            else
            {
                tcpServer.SyncSendToAll(bufferList);
                SendDataMessageCompleteHandler(tcpServer, token);
            }
            return token.Task;
        }

        private void SendDataMessageCompleteHandler(object sender, TaskCompletionSourceToken token)
        {
            if (!_replyExpectedMsgs.ContainsKey(token.Id))
            {
                token.SetResult(null);
                return;
            }
            try
            {
                if (!token.Task.Wait(T3))
                {
                    Logger.Error($"T3 Timeout[id=0x{token.Id:X8}]: {T3 / 1000} sec.");
                    token.SetException(new SecsException(token.MessageSent, Resources.T3Timeout));
                }
            }
            catch (AggregateException) { }
            finally
            {
                _replyExpectedMsgs.TryRemove(token.Id, out _);
            }
        }
        /// <summary>
        /// Asynchronously send message to device .
        /// </summary>
        /// <param name="secsMessage">primary message</param>
        /// <returns>senondary</returns>
        public Task<SecsMessage> SendAsync(SecsMessage secsMessage) => SendAsync(secsMessage, NewSystemId);
        /// <summary>
        /// Asynchronously send message to device .
        /// </summary>
        /// <param name="secsMessage"></param>
        /// <param name="systemId"></param>
        /// <returns>null</returns>
        public Task<SecsMessage> SendAsync(SecsMessage secsMessage, int systemId) => SendDataMessageAsync(secsMessage, systemId);
        #endregion

        #region dispose
        private const int DisposalNotStarted = 0;
        private const int DisposalComplete = 1;
        private int _disposeStage;
        private StreamDecoder _secsDecoder;
        private static readonly ArraySegment<byte> ControlMessageLengthBytes = new ArraySegment<byte>(new byte[] { 0, 0, 0, 10 });

        public bool IsDisposed => Interlocked.CompareExchange(ref _disposeStage, DisposalComplete, DisposalComplete) == DisposalComplete;

        public int DecoderBufferSize { get; private set; }

        private static readonly SecsMessage ControlMessage = new SecsMessage(0, 0, String.Empty);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeStage, DisposalComplete) != DisposalNotStarted)
                return;

            ConnectionChanged = null;
            if (State == ConnectionState.Selected)
                SendControlMessage(MessageType.SeperateRequest, NewSystemId);
            Reset();
            _timer7.Dispose();
            _timer8.Dispose();
            _timerLinkTest.Dispose();
        }
        #endregion
    }
}
