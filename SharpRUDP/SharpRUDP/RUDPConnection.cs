using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpRUDP
{
    public class RUDPConnection : RUDPSocket
    {
        #region Variables
        public bool IsServer { get; set; }
        public bool IsClient { get { return !IsServer; } }
        public ConnectionState State { get; set; }
        public int RecvFrequencyMs { get; set; }
        public int KeepAliveInterval { get; set; }
        public int ConnectionTimeout { get; set; }
        public int PacketIdLimit { get; set; }
        public int SequenceLimit { get; set; }
        public int ClientStartSequence { get; set; }
        public int ServerStartSequence { get; set; }
        public int MTU { get; set; }
        public byte[] PacketHeader { get; set; }
        public byte[] InternalPacketHeader { get; set; }
        public Dictionary<string, RUDPConnectionData> Connections { get; set; }

        public delegate void dlgEventVoid();
        public delegate void dlgEventConnection(IPEndPoint ep);
        public delegate void dlgEventPacket(RUDPPacket p);
        public delegate void dlgEventPacketId(int idPacket);
        public delegate void dlgEventError(IPEndPoint ep, Exception ex);
        public event dlgEventConnection OnClientConnect;
        public event dlgEventConnection OnClientDisconnect;
        public event dlgEventConnection OnConnected;
        public event dlgEventConnection OnDisconnected;
        public event dlgEventPacket OnPacketReceived;
        public event dlgEventPacketId OnPacketConfirmed;
        public event dlgEventError OnSocketError;

        private bool _isAlive = false;
        private int _maxMTU { get { return (int)(MTU * 0.80); } }
        private object _debugMutex = new object();
        private SpinWait _sw = new SpinWait();
        private Thread _thRecv;
        private Thread _thKeepAlive;
        private AutoResetEvent SignalPacketRecv = new AutoResetEvent(false);
        #endregion

        #region Initialization
        public RUDPConnection()
        {
            IsServer = false;
            MTU = 1024 * 8;
            RecvFrequencyMs = 10;
            PacketIdLimit = int.MaxValue / 2;
            SequenceLimit = int.MaxValue / 2;
            ClientStartSequence = 100;
            ServerStartSequence = 200;
            KeepAliveInterval = 1;
            ConnectionTimeout = 5;
            State = ConnectionState.CLOSED;
            PacketHeader = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
            InternalPacketHeader = new byte[] { 0xFA, 0xCE, 0xFE, 0xED };
        }

        private void Debug(object obj, params object[] args)
        {
            //return;
            lock (_debugMutex)
            {
                Console.ForegroundColor = IsServer ? ConsoleColor.Cyan : ConsoleColor.Green;
                RUDPLogger.Info(IsServer ? "[S]" : "[C]", obj, args);
                Console.ResetColor();
            }
        }

        public void Connect(string address, int port)
        {
            IsServer = false;
            State = ConnectionState.OPENING;
            Client(address, port);
            Initialize();
            Send(RemoteEndPoint, RUDPPacketType.SYN);
        }

        public void Listen(string address, int port)
        {
            IsServer = true;
            Server(address, port);
            State = ConnectionState.LISTEN;
            Initialize();
        }

        public virtual void Initialize(bool startThreads = true)
        {
            _isAlive = true;
            Connections = new Dictionary<string, RUDPConnectionData>();
            InitThreads(startThreads);
        }

        public override void SocketError(IPEndPoint ep, Exception ex)
        {
            base.SocketError(ep, ex);
            OnSocketError?.Invoke(ep, ex);
        }

        public void InitThreads(bool start)
        {
            _thRecv = new Thread(() =>
            {
                while (_isAlive)
                {
                    ProcessRecvQueue();
                    SignalPacketRecv.WaitOne(10);
                }
            });
            _thKeepAlive = new Thread(() =>
            {
                while (_isAlive)
                {
                    List<string> offlineEndpoints = new List<string>();
                    List<RUDPConnectionData> cons = new List<RUDPConnectionData>();
                    lock(Connections)
                        Parallel.ForEach(Connections, (kvp) =>
                        {
                            cons.Add(kvp.Value);
                        });
                    Parallel.ForEach(cons, (cn) =>
                    {
                        if ((DateTime.Now - cn.LastPacketDate).Seconds > ConnectionTimeout)
                        {
                            Debug("TIMEOUT");
                            if (!IsServer)
                                Disconnect();
                            else
                                offlineEndpoints.Add(cn.EndPoint.ToString());
                        }
                        else if ((DateTime.Now - cn.LastPacketDate).Seconds > KeepAliveInterval)
                        {
                            SendKeepAlive(cn.EndPoint);
                            lock (cn.Pending)
                                foreach (RUDPPacket p in cn.Pending)
                                    RetransmitPacket(p);
                        }
                    });
                    lock (Connections)
                        foreach (string ep in offlineEndpoints)
                        {
                            OnClientDisconnect?.Invoke(Connections[ep].EndPoint);
                            Connections.Remove(ep);
                        }
                    Thread.Sleep(KeepAliveInterval * 1000);
                }
            });
            if (start)
            {
                _thRecv.Start();
                _thKeepAlive.Start();
            }
        }
        #endregion

        #region Connections
        public void Disconnect()
        {
            if (State >= ConnectionState.CLOSING)
                return;
            State = ConnectionState.CLOSING;
            _isAlive = false;
            Debug("DISCONNECT");
            new Thread(() =>
            {
                try
                {
                    _socket.Shutdown(SocketShutdown.Both);
                }
                catch (Exception) { }
                if (IsServer)
                    _socket.Close();
                if (_thRecv != null)
                    while (_thRecv.IsAlive)
                        Thread.Sleep(10);
                if (_thKeepAlive != null)
                    while (_thKeepAlive.IsAlive)
                        Thread.Sleep(10);
                State = ConnectionState.CLOSED;
                if (!IsServer)
                    OnDisconnected?.Invoke(RemoteEndPoint);
            }).Start();
        }

        private RUDPConnectionData GetConnection(RUDPPacket p)
        {
            return GetConnection((p.Src == null) ? p.Dst : p.Src);
        }

        private RUDPConnectionData GetConnection(IPEndPoint ep)
        {
            lock (Connections)
            {
                if (!Connections.ContainsKey(ep.ToString()))
                {
                    if (!Connections.ContainsKey(ep.ToString()))
                    {
                        Connections[ep.ToString()] = new RUDPConnectionData()
                        {
                            EndPoint = ep,
                            Local = IsServer ? ServerStartSequence : ClientStartSequence,
                            Remote = IsServer ? ClientStartSequence : ServerStartSequence
                        };
                        while (!Connections.ContainsKey(ep.ToString()))
                            Thread.Sleep(1);
                        Debug("NEW CONNECTION: {0}", Connections[ep.ToString()]);
                    }
                }
            }
            return Connections[ep.ToString()];
        }
        #endregion

        #region Send
        public void Send(string data)
        {
            Send(RemoteEndPoint, RUDPPacketType.DAT, RUDPPacketFlags.NUL, Encoding.ASCII.GetBytes(data));
        }

        public int Send(IPEndPoint destination, RUDPPacketType type = RUDPPacketType.DAT, RUDPPacketFlags flags = RUDPPacketFlags.NUL, byte[] data = null, int[] intData = null)
        {
            RUDPPacket packet = null;
            bool reset = false;
            RUDPConnectionData cn = GetConnection(destination);
            if ((data != null && data.Length < _maxMTU) || data == null)
            {
                packet = new RUDPPacket()
                {
                    Dst = destination,
                    Id = cn.PacketId,
                    Type = type,
                    Flags = flags,
                    Data = data,
                    intData = intData
                };
                SendPacket(packet);
                cn.PacketId++;
                if (!IsServer && cn.Local > SequenceLimit)
                    reset = true;
            }
            else if (data != null && data.Length >= _maxMTU)
            {
                int i = 0;
                List<RUDPPacket> PacketsToSend = new List<RUDPPacket>();
                while (i < data.Length)
                {
                    int min = i;
                    int max = _maxMTU;
                    if ((min + max) > data.Length)
                        max = data.Length - min;
                    byte[] buf = data.Skip(i).Take(max).ToArray();
                    PacketsToSend.Add(new RUDPPacket()
                    {
                        Dst = destination,
                        Id = cn.PacketId,
                        Type = type,
                        Flags = flags,
                        Data = buf,
                        intData = intData
                    });
                    i += _maxMTU;
                }
                foreach (RUDPPacket p in PacketsToSend)
                {
                    p.Qty = PacketsToSend.Count;
                    SendPacket(p);
                }
                cn.PacketId++;
                if (!IsServer && cn.Local > SequenceLimit)
                    reset = true;
                packet = PacketsToSend.First();
            }
            else
                throw new Exception("This should not happen");
            if (cn.PacketId > PacketIdLimit)
                cn.PacketId = 0;
            if (reset)
            {
                SendPacket(new RUDPPacket()
                {
                    Dst = destination,
                    Type = RUDPPacketType.RST
                });
                cn.Local = IsServer ? ServerStartSequence : ClientStartSequence;
            }
            return packet.Id;
        }

        private void RetransmitPacket(RUDPPacket p)
        {
            p.Retransmit = true;
            SendPacket(p);
        }

        private void SendPacket(RUDPPacket p)
        {
            DateTime dtNow = DateTime.Now;
            RUDPConnectionData cn = GetConnection(p.Dst);
            if (!p.Retransmit)
            {
                p.Seq = cn.Local;
                cn.Local++;
                p.Sent = dtNow;
                lock (cn.Pending)
                    cn.Pending.Add(p);
                _sw.SpinOnce();
                Debug("SEND -> {0}: {1}", p.Dst, p);
            }
            else { Debug("RETRANSMIT -> {0}: {1}", p.Dst, p); }
            Send(p.Dst, p.ToByteArray(PacketHeader));
        }

        public void SendKeepAlive(IPEndPoint ep)
        {
            Send(ep, new RUDPInternalPackets.ACKPacket() { header = InternalPacketHeader, sequence = 0 }.Serialize());
        }
        #endregion

        #region Receive
        public override void PacketReceive(IPEndPoint ep, byte[] data, int length)
        {
            base.PacketReceive(ep, data, length);
            DateTime dtNow = DateTime.Now;
            if (length > PacketHeader.Length && data.Take(PacketHeader.Length).SequenceEqual(PacketHeader))
            {
                RUDPPacket p = RUDPPacket.Deserialize(PacketHeader, data);
                p.Src = IsServer ? ep : RemoteEndPoint;
                p.Received = DateTime.Now;
                RUDPConnectionData cn = GetConnection(p.Src);
                cn.LastPacketDate = dtNow;
                Send(p.Src, new RUDPInternalPackets.ACKPacket() { header = InternalPacketHeader, sequence = p.Seq }.Serialize());
                //Debug("ACK SEND -> {0}: {1}", p.Src, p.Seq);
                lock (cn.ReceivedPackets)
                    cn.ReceivedPackets.Add(p);
                SignalPacketRecv.Set();
            }
            else if (length > InternalPacketHeader.Length && data.Take(InternalPacketHeader.Length).SequenceEqual(InternalPacketHeader))
            {
                IPEndPoint src = IsServer ? ep : RemoteEndPoint;
                RUDPConnectionData cn = GetConnection(src);
                cn.LastPacketDate = dtNow;
                RUDPInternalPackets.ACKPacket ack = RUDPInternalPackets.ACKPacket.Deserialize(data);
                //Debug("ACK RECV <- {0}: {1}", src, ack.sequence);
                lock (cn.Pending)
                    cn.Pending.RemoveAll(x => x.Seq == ack.sequence);
            }
            else
                Console.WriteLine("[{0}] RAW RECV: [{1}]", GetType().ToString(), Encoding.ASCII.GetString(data, 0, length));
        }

        public void ProcessRecvQueue()
        {
            foreach (var cdata in Connections)
            {
                RUDPConnectionData cn = cdata.Value;

                List<RUDPPacket> PacketsToRecv = new List<RUDPPacket>();
                lock (cn.ReceivedPackets)
                    PacketsToRecv.AddRange(cn.ReceivedPackets.OrderBy(x => x.Seq));
                PacketsToRecv = PacketsToRecv.GroupBy(x => x.Seq).Select(g => g.First()).ToList();

                List<int> idsToConfirm = new List<int>();

                foreach (RUDPPacket p in PacketsToRecv)
                {
                    lock (cn.ReceivedPackets)
                        cn.ReceivedPackets.Remove(p);

                    if (p.Processed)
                        continue;

                    if (p.Seq < cn.Remote)
                    {
                        cn.ReceivedPackets.Add(p);
                        continue;
                    }

                    if (p.Seq > cn.Remote)
                    {
                        cn.ReceivedPackets.Add(p);
                        break;
                    }

                    Debug("RECV <- {0}: {1}", p.Src, p);

                    if (p.Qty == 0)
                    {
                        cn.Remote++;
                        p.Processed = true;

                        if (p.Type == RUDPPacketType.SYN)
                        {
                            if (IsServer)
                            {
                                Send(p.Src, RUDPPacketType.SYN, RUDPPacketFlags.ACK);
                                OnClientConnect?.Invoke(p.Src);
                            }
                            else if (p.Flags == RUDPPacketFlags.ACK)
                            {
                                State = ConnectionState.OPEN;
                                OnConnected?.Invoke(p.Src);
                            }
                            continue;
                        }

                        if (p.Type == RUDPPacketType.RST)
                        {
                            cn.Remote = IsServer ? ClientStartSequence : ServerStartSequence;
                            continue;
                        }

                        if (p.Type == RUDPPacketType.DAT)
                        {
                            idsToConfirm.Add(p.Id);
                            OnPacketReceived?.Invoke(p);
                        }

                        if(p.Type == RUDPPacketType.ACK)
                            lock(cn.Pending)
                                foreach(int packetId in p.intData)
                                    OnPacketConfirmed?.Invoke(packetId);
                    }
                    else
                    {
                        // Multipacket!
                        List<RUDPPacket> multiPackets = PacketsToRecv.Where(x => x.Id == p.Id).ToList();
                        if (multiPackets.Count == p.Qty)
                        {
                            Debug("MULTIPACKET {0}", p.Id);

                            byte[] buf;
                            using (MemoryStream ms = new MemoryStream())
                            {
                                using (BinaryWriter bw = new BinaryWriter(ms))
                                    foreach (RUDPPacket mp in multiPackets)
                                    {
                                        mp.Processed = true;
                                        bw.Write(mp.Data);
                                        Debug("RECV MP <- {0}: {1}", p.Src, mp);
                                    }
                                buf = ms.ToArray();
                            }
                            Debug("MULTIPACKET ID {0} DATA: {1}", p.Id, Encoding.ASCII.GetString(buf));

                            idsToConfirm.Add(p.Id);
                            OnPacketReceived?.Invoke(new RUDPPacket()
                            {
                                Retransmit = p.Retransmit,
                                Sent = p.Sent,
                                Data = buf,
                                Dst = p.Dst,
                                Flags = p.Flags,
                                Id = p.Id,
                                Qty = p.Qty,
                                Received = p.Received,
                                Seq = p.Seq,
                                Src = p.Src,
                                Type = p.Type
                            });

                            cn.Remote += p.Qty;
                        }
                        else
                        {
                            if (multiPackets.Count < p.Qty)
                            {
                                cn.ReceivedPackets.Add(p);
                                break;
                            }
                            else
                            {
                                Debug("P.QTY > MULTIPACKETS.COUNT ({0} > {1})", p.Qty, multiPackets.Count);
                                throw new Exception();
                            }
                        }
                    }
                }
                if(idsToConfirm.Count > 0)
                    Send(cn.EndPoint, RUDPPacketType.ACK, RUDPPacketFlags.NUL, null, idsToConfirm.ToArray());
            }
        }
        #endregion
    }
}