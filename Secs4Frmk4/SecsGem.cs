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
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Granda.HSMS.Properties;

namespace Granda.HSMS
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

        #endregion

        #region ctor and start/stop methods
        public SecsGem(bool isActive, IPAddress ip, int port)
        {
            IsActive = isActive;
            IpAddress = ip;
            Port = port;
            _secsDecoder = new StreamDecoder(0x4000, HandlerControlMessage, HandleDataMessage);
            DecoderBufferSize = 0x4000;
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

            if (IsActive)
            {
                _startImpl = () =>
                {
                    if (IsDisposed)
                        return;
                    CommunicationStateChanging(ConnectionState.Connecting);
                    try
                    {
                        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        SocketAsyncEventArgs socketAsyncEventArgs = new SocketAsyncEventArgs();
                        socketAsyncEventArgs.RemoteEndPoint = new IPEndPoint(ip, port);
                        socketAsyncEventArgs.Completed += ((sender, e) =>
                    {
                        if (e.SocketError == SocketError.Success)
                        {
                            // hook receive envent first, because no message will received before 'SelectRequest' send to device
                            StartSocketReceive();
                            SendControlMessage(MessageType.SelectRequest, NewSystemId);
                        }
                        else
                        {
                            Logger.Info($"Start T5 Timer: {T5 / 1000} sec.");
                            Thread.Sleep(T5);
                            CommunicationStateChanging(ConnectionState.Retry);
                        }
                    });
                        _socket.ConnectAsync(socketAsyncEventArgs);
                    }
                    catch (Exception ex)
                    {
                        if (IsDisposed)
                            return;
                        Logger.Error("", ex);
                        Logger.Info($"Start T5 Timer: {T5 / 1000} sec.");
                        Thread.Sleep(T5);
                    }

                };
            }
            else
            {
                var server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                server.Bind(new IPEndPoint(IpAddress, port));
                server.Listen(0);

                _startImpl = () =>
                {
                    if (IsDisposed) return;
                    CommunicationStateChanging(ConnectionState.Connecting);
                    try
                    {
                        var socketAsyncEventArgs = new SocketAsyncEventArgs();
                        socketAsyncEventArgs.Completed += (sender, e) =>
                        {
                            if (e.SocketError == SocketError.Success)
                            {
                                _socket = e.AcceptSocket;
                                StartSocketReceive();
                            }
                            else
                            {
                                Logger.Info($"Start T5 Timer: {T5 / 1000} sec.");
                                Thread.Sleep(T5);
                                CommunicationStateChanging(ConnectionState.Retry);
                            }
                        };
                        server.AcceptAsync(socketAsyncEventArgs);
                    }
                    catch (Exception ex)
                    {
                        if (IsDisposed)
                            return;
                        Logger.Error("", ex);
                    }
                };
                _stopImpl = () =>
                {
                    if (IsDisposed)
                        server.Dispose();
                };
            }
        }



        public void Start() => new TaskFactory(TaskScheduler.Default).StartNew(_startImpl);

        public void Stop() => new TaskFactory(TaskScheduler.Default).StartNew(_stopImpl);

        private void Reset()
        {
            _timer7.Change(Timeout.Infinite, Timeout.Infinite);
            _timer8.Change(Timeout.Infinite, Timeout.Infinite);
            _timerLinkTest.Change(Timeout.Infinite, Timeout.Infinite);

            _secsDecoder.Reset();
            _replyExpectedMsgs.Clear();
            _stopImpl?.Invoke();

            if (_socket is null)
                return;
            if (_socket.Connected)
                _socket.Shutdown(SocketShutdown.Both);
            _socket.Dispose();
            _socket = null;
        }

        #endregion

        #region connection
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
                    Task.Factory.StartNew(_startImpl);
                    break;
                default:
                    break;
            }
        }
        #endregion

        #region receive
        private void StartSocketReceive()
        {
            CommunicationStateChanging(ConnectionState.Connected);
            var receiveCompleteEventArgs = new SocketAsyncEventArgs();
            receiveCompleteEventArgs.SetBuffer(_secsDecoder.Buffer, _secsDecoder.BufferOffset, _secsDecoder.BufferCount);
            receiveCompleteEventArgs.Completed += ReceiveCompleteEventArgs_Completed;
            if (!_socket.ReceiveAsync(receiveCompleteEventArgs))
                ReceiveCompleteEventArgs_Completed(_socket, receiveCompleteEventArgs);
        }

        private void ReceiveCompleteEventArgs_Completed(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError != SocketError.Success)
            {
                var ex = new SocketException((int)e.SocketError);
                Logger.Error($"RecieveComplete socket error:{ex.Message}, ErrorCode:{ex.SocketErrorCode}", ex);
                CommunicationStateChanging(ConnectionState.Retry);
                return;
            }

            try
            {
                _timer8.Change(Timeout.Infinite, Timeout.Infinite);
                var receiveCount = e.BytesTransferred;
                if (receiveCount == 0)
                {
                    Logger.Error("receive 0 byte.");
                    CommunicationStateChanging(ConnectionState.Retry);
                    return;
                }

                if (_secsDecoder.Decode(receiveCount))
                {
#if !DISABLE_T8
                    Trace.WriteLine($"Start T8 Timer: {T8 / 1000} sec.");
                    _timer8.Change(T8, Timeout.Infinite);
#endif
                }

                if (_secsDecoder.Buffer.Length != DecoderBufferSize)
                {
                    // buffer size changed
                    e.SetBuffer(_secsDecoder.Buffer, _secsDecoder.BufferOffset, _secsDecoder.BufferCount);
                    DecoderBufferSize = _secsDecoder.Buffer.Length;
                }
                else
                {
                    e.SetBuffer(_secsDecoder.BufferOffset, _secsDecoder.BufferCount);
                }

                if (_socket is null || IsDisposed)
                    return;

                if (!_socket.ReceiveAsync(e))
                    ReceiveCompleteEventArgs_Completed(sender, e);
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
                SecsMessage replyWrongDeviceIdMsg = new SecsMessage(
                    9,
                    1,
                    false,
                    Resources.S9F1,
                    Item.B(header.EncodeTo(new byte[10])));// 将header作为消息体返回
                SendDataMessageAsync(replyWrongDeviceIdMsg, NewSystemId);

                if (_replyExpectedMsgs.TryGetValue(systemByte, out var ar1))
                    ar1.HandleReplyMessage(replyWrongDeviceIdMsg);
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
                    CommunicationStateChanging(ConnectionState.Selected);
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

            var sendEventArgs = new SocketAsyncEventArgs
            {
                BufferList = new List<ArraySegment<byte>>(2)
                {
                    ControlMessageLengthBytes,
                    new ArraySegment<byte>(new MessageHeader
                    {
                        DeviceId = 0xffff,
                        MessageType = messageType,
                        SystemBytes = systemByte
                    }.EncodeTo(new byte[10]))
                },
                UserToken = token,
            };

            sendEventArgs.Completed += SendControlMessage_Completed;
            if (!_socket.SendAsync(sendEventArgs))
                SendControlMessage_Completed(_socket, sendEventArgs);
        }

        private void SendControlMessage_Completed(object sender, SocketAsyncEventArgs e)
        {
            var completeToken = e.UserToken as TaskCompletionSourceToken;
            if (e.SocketError != SocketError.Success)
            {
                completeToken.SetException(new SocketException((int)e.SocketError));
                return;
            }

            Logger.Info("Sent Control message: " + completeToken.MsgType);
            if (_replyExpectedMsgs.ContainsKey(completeToken.Id))
            {
                if (!completeToken.Task.Wait(T6))
                {
                    Logger.Error($"T6 Timeout: {T6 / 1000} sec.");
                    CommunicationStateChanging(ConnectionState.Retry);
                }
                _replyExpectedMsgs.TryRemove(completeToken.Id, out TaskCompletionSourceToken _);
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
            var sendEventArgs = new SocketAsyncEventArgs
            {
                BufferList = bufferList,
                UserToken = token,
            };
            sendEventArgs.Completed += SendDataMessage_Completed;
            if (!_socket.SendAsync(sendEventArgs))
                SendDataMessage_Completed(_socket, sendEventArgs);
            return token.Task;
        }

        private void SendDataMessage_Completed(object sender, SocketAsyncEventArgs e)
        {
            var completeToken = e.UserToken as TaskCompletionSourceToken;
            if (e.SocketError != SocketError.Success)
            {
                completeToken.SetException(new SocketException((int)e.SocketError));
                CommunicationStateChanging(ConnectionState.Retry);
                return;
            }

            Trace.WriteLine("Send Data Message: " + completeToken.MessageSent.ToString());
            if (!_replyExpectedMsgs.ContainsKey(completeToken.Id))
            {
                completeToken.SetResult(null);
                return;
            }

            try
            {
                if (!completeToken.Task.Wait(T3))
                {
                    Logger.Error($"T3 Timeout[id=0x{completeToken.Id:X8}]: {T3 / 1000} sec.");
                    completeToken.SetException(new SecsException(completeToken.MessageSent, Resources.T3Timeout));
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                _replyExpectedMsgs.TryRemove(completeToken.Id, out TaskCompletionSourceToken _);
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
        private Socket _socket;
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
