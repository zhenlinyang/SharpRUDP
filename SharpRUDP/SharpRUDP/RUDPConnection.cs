using SharpRUDP.Serializers;
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
        public bool DebugEnabled { get; set; }

        public int Port { get; set; }
        public string Address { get; set; }
        public bool IsServer { get; set; }
        public bool IsClient { get { return !IsServer; } }
        public ConnectionState State { get; set; }
        public int RecvWaitMs { get; set; }
        public int KeepAliveInterval { get; set; }
        public int ConnectionTimeout { get; set; }
        public int PacketIdLimit { get; set; }
        public int SequenceLimit { get; set; }
        public int ClientStartSequence { get; set; }
        public int ServerStartSequence { get; set; }
        public int MTU { get; set; }
        public byte[] PacketHeader { get; set; }
        public byte[] AckPacketHeader { get; set; }
        public byte[] PingPacketHeader { get; set; }
        public Dictionary<string, RUDPConnectionData> Connections { get; set; }
        public RUDPSerializeMode SerializeMode
        {
            get
            {
                if (_serializer.GetType() == typeof(RUDPJSONSerializer))
                    return RUDPSerializeMode.JSON;
                if (_serializer.GetType() == typeof(RUDPBinarySerializer))
                    return RUDPSerializeMode.Binary;
                SerializeMode = RUDPSerializeMode.JSON;
                return RUDPSerializeMode.JSON;
            }
            set
            {
                switch (value)
                {
                    case RUDPSerializeMode.JSON:
                        _serializer = new RUDPJSONSerializer();
                        break;
                    case RUDPSerializeMode.Binary:
                        _serializer = new RUDPBinarySerializer();
                        break;
                }
            }
        }

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
        public event dlgEventError OnSocketError;

        private bool _isAlive = false;
        private int _maxMTU { get { return (int)(MTU * 0.80); } }
        private static object _debugMutex = new object();
        private object _connectionsMutex = new object();
        private SpinWait _sw = new SpinWait();
        private Thread _thRecv;
        private Thread _thKeepAlive;
        private RUDPSerializer _serializer;
        private AutoResetEvent SignalPacketRecv = new AutoResetEvent(false);
        #endregion

        #region Initialization
        public RUDPConnection()
        {
            DebugEnabled = true;
            IsServer = false;
            MTU = 1024 * 8;
            RecvWaitMs = 10;
            PacketIdLimit = int.MaxValue / 2;
            SequenceLimit = int.MaxValue / 2;
            ClientStartSequence = 100;
            ServerStartSequence = 200;
            KeepAliveInterval = 1;
            ConnectionTimeout = 5;
            State = ConnectionState.CLOSED;
            PacketHeader = new byte[] { 0xFF, 0x01 };
            AckPacketHeader = new byte[] { 0xFF, 0x02 };
            PingPacketHeader = new byte[] { 0xFF, 0x03 };
            SerializeMode = RUDPSerializeMode.Binary;
        }

        private void Debug(object obj, params object[] args)
        {
            if (!DebugEnabled)
                return;
            lock (_debugMutex)
            {
                Console.ForegroundColor = IsServer ? ConsoleColor.Cyan : ConsoleColor.Green;
                RUDPLogger.Info(IsServer ? "[S]" : "[C]", obj, args);
                Console.ResetColor();
            }
        }

        public void Connect(string address, int port)
        {
            Port = port;
            Address = address;
            IsServer = false;
            State = ConnectionState.OPENING;
            Client(address, port);
            Initialize();
            Send(RemoteEndPoint, RUDPPacketType.SYN);
        }

        public void Listen(string address, int port)
        {
            Port = port;
            Address = address;
            IsServer = true;
            Server(address, port);
            State = ConnectionState.LISTEN;
            Initialize();
        }

        public virtual void Initialize()
        {
            _isAlive = true;
            Connections = new Dictionary<string, RUDPConnectionData>();
            InitThreads();
        }

        public override void SocketError(IPEndPoint ep, Exception ex)
        {
            base.SocketError(ep, ex);
            OnSocketError?.Invoke(ep, ex);
        }

        public void InitThreads()
        {
            _thRecv = new Thread(() =>
            {
                while (_isAlive)
                {
                    ProcessRecvQueue();
                    SignalPacketRecv.WaitOne(RecvWaitMs);
                }
            });
            _thKeepAlive = new Thread(() =>
            {
                while (_isAlive)
                {
                    DateTime dtNow = DateTime.Now;
                    List<string> offlineEndpoints = new List<string>();
                    List<RUDPConnectionData> cons = new List<RUDPConnectionData>();
                    lock (_connectionsMutex)
                        cons.AddRange(Connections.Select(x => x.Value));
                    Parallel.ForEach(cons, (cn) =>
                    {
                        if ((dtNow - cn.LastKeepAliveDate).Seconds >= KeepAliveInterval)
                        {
                            cn.LastKeepAliveDate = dtNow;
                            SendKeepAlive(cn.EndPoint);
                            lock (cn.Pending)
                                foreach (RUDPPacket p in cn.Pending)
                                    RetransmitPacket(p);
                        }
                        if ((dtNow - cn.LastPacketDate).Seconds >= ConnectionTimeout)
                        {
                            Debug("TIMEOUT");
                            if (!IsServer)
                                Disconnect();
                            else
                                offlineEndpoints.Add(cn.EndPoint.ToString());
                        }
                    });
                    lock (_connectionsMutex)
                        foreach (string ep in offlineEndpoints)
                        {
                            OnClientDisconnect?.Invoke(Connections[ep].EndPoint);
                            Debug("-Connection {0}", ep);
                            Connections.Remove(ep);
                        }
                    Thread.Sleep(10);
                }
            });
            _thRecv.Start();
            _thKeepAlive.Start();
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
                    if (IsServer)
                        _socket.Close();
                }
                catch (Exception) { }
                if (_thRecv != null)
                    while (_thRecv.IsAlive)
                        Thread.Sleep(10);
                if (_thKeepAlive != null)
                    while (_thKeepAlive.IsAlive)
                        Thread.Sleep(10);
                State = ConnectionState.CLOSED;
                OnDisconnected?.Invoke(IsServer ? RemoteEndPoint : LocalEndPoint);
            }).Start();
        }

        private RUDPConnectionData GetConnection(RUDPPacket p)
        {
            return GetConnection((p.Src == null) ? p.Dst : p.Src);
        }

        private RUDPConnectionData GetConnection(IPEndPoint ep)
        {
            lock (_connectionsMutex)
            {
                if (!Connections.ContainsKey(ep.ToString()))
                {
                    Connections.Add(ep.ToString(), new RUDPConnectionData()
                    {
                        EndPoint = ep,
                        Local = IsServer ? ServerStartSequence : ClientStartSequence,
                        Remote = IsServer ? ClientStartSequence : ServerStartSequence
                    });
                    Debug("+Connection {0}", Connections[ep.ToString()]);
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
            if (!_isAlive)
                return -1;
            RUDPPacket packet = null;
            bool reset = false;
            RUDPConnectionData cn = GetConnection(destination);
            if ((data != null && data.Length < _maxMTU) || data == null)
            {
                packet = new RUDPPacket()
                {
                    Serializer = _serializer,
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
                        Serializer = _serializer,
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
                    Serializer = _serializer,
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
            RUDPConnectionData cn = GetConnection(p.Dst);
            if (!p.Retransmit)
            {
                p.Seq = cn.Local;
                cn.Local++;
                p.Sent = DateTime.Now;
                lock (cn.Pending)
                    cn.Pending.Add(p);
                _sw.SpinOnce();
                Debug("SEND -> {0}: {1}", p.Dst, p);
            }
            else { Debug("RETRANSMIT -> {0}: {1}", p.Dst, p); }
            Send(p.Dst, _serializer.Serialize(PacketHeader, p));
        }

        public void SendKeepAlive(IPEndPoint ep)
        {
            Send(ep, new RUDPInternalPackets.PingPacket() { header = PingPacketHeader }.Serialize());
        }
        #endregion

        #region Receive
        public override void PacketReceive(IPEndPoint ep, byte[] data, int length)
        {
            base.PacketReceive(ep, data, length);
            DateTime dtNow = DateTime.Now;
            IPEndPoint src = IsServer ? ep : RemoteEndPoint;
            if (length >= PacketHeader.Length && data.Take(PacketHeader.Length).SequenceEqual(PacketHeader))
            {
                RUDPPacket p = RUDPPacket.Deserialize(_serializer, PacketHeader, data);
                RUDPConnectionData cn = GetConnection(src);
                cn.LastPacketDate = dtNow;
                p.Src = src;
                p.Serializer = _serializer;
                p.Received = dtNow;
                lock (cn.ReceivedPackets)
                    cn.ReceivedPackets.Add(p);
                Send(p.Src, new RUDPInternalPackets.AckPacket() { header = AckPacketHeader, sequence = p.Seq }.Serialize());
                //Debug("ACK SEND -> {0}: {1}", p.Src, p.Seq);
                SignalPacketRecv.Set();
            }
            else if (length >= AckPacketHeader.Length && data.Take(AckPacketHeader.Length).SequenceEqual(AckPacketHeader))
            {
                RUDPConnectionData cn = GetConnection(src);
                cn.LastPacketDate = dtNow;
                RUDPInternalPackets.AckPacket ack = RUDPInternalPackets.AckPacket.Deserialize(data);
                //Debug("ACK RECV <- {0}: {1}", src, ack.sequence);
                lock (cn.Pending)
                    cn.Pending.RemoveAll(x => x.Seq == ack.sequence);
            }
            else if (length >= PingPacketHeader.Length && data.Take(PingPacketHeader.Length).SequenceEqual(PingPacketHeader))
            {
                RUDPConnectionData cn = GetConnection(src);
                cn.LastPacketDate = dtNow;
                RUDPInternalPackets.PingPacket ping = RUDPInternalPackets.PingPacket.Deserialize(data);
                Debug("<- PING FROM {0}", src);
            }
            else
                Console.WriteLine("[{0}] RAW RECV: [{1}]", GetType().ToString(), Encoding.ASCII.GetString(data, 0, length));
        }

        public void ProcessRecvQueue()
        {
            List<RUDPConnectionData> connections = new List<RUDPConnectionData>();
            lock (_connectionsMutex)
                connections.AddRange(Connections.Select(x => x.Value));
            foreach (RUDPConnectionData cn in connections)
            {
                List<RUDPPacket> PacketsToRecv = new List<RUDPPacket>();
                lock (cn.ReceivedPackets)
                    PacketsToRecv.AddRange(cn.ReceivedPackets.OrderBy(x => x.Seq));
                PacketsToRecv = PacketsToRecv.GroupBy(x => x.Seq).Select(g => g.First()).ToList();
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
                            break;
                        }

                        if (p.Type == RUDPPacketType.DAT)
                            OnPacketReceived?.Invoke(p);
                    }
                    else
                    {
                        List<RUDPPacket> multiPackets = PacketsToRecv.Where(x => x.Id == p.Id).ToList();
                        if (multiPackets.Count == p.Qty)
                        {
                            Debug("MULTIPACKET {0}", p.Id);

                            byte[] buf;
                            MemoryStream ms = new MemoryStream();
                            using (BinaryWriter bw = new BinaryWriter(ms))
                                foreach (RUDPPacket mp in multiPackets)
                                {
                                    mp.Processed = true;
                                    bw.Write(mp.Data);
                                    Debug("RECV MP <- {0}: {1}", p.Src, mp);
                                }
                            buf = ms.ToArray();
                            Debug("MULTIPACKET ID {0} DATA: {1}", p.Id, Encoding.ASCII.GetString(buf));

                            OnPacketReceived?.Invoke(new RUDPPacket()
                            {
                                Serializer = _serializer,
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
                        else if (multiPackets.Count < p.Qty)
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
        }
        #endregion
    }
}