using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SharpRUDP
{
    public class RUDPConnection : RUDPSocket
    {
        public bool IsServer { get; set; }
        public bool IsClient { get { return !IsServer; } }
        public int Port { get; set; }
        public string Address { get; set; }
        public ConnectionState State { get; set; }
        public int RecvFrequencyMs { get; set; }
        public int PacketIdLimit { get; set; }
        public int SequenceLimit { get; set; }
        public int ClientStartSequence { get; set; }
        public int ServerStartSequence { get; set; }
        public int MTU { get; set; }
        public byte[] PacketHeader { get; set; }
        public byte[] InternalACKHeader { get; set; }
        public byte[] InternalKeepAliveHeader { get; set; }
        public int ConnectionTimeout { get; set; }

        public delegate void dlgEventVoid();
        public delegate void dlgEventConnection(IPEndPoint ep);
        public delegate void dlgEventUserData(RUDPPacket p);
        public event dlgEventConnection OnClientConnect;
        public event dlgEventConnection OnClientDisconnect;
        public event dlgEventConnection OnConnected;
        public event dlgEventConnection OnDisconnected;
        public event dlgEventUserData OnPacketReceived;

        private Dictionary<string, IPEndPoint> _clients { get; set; }
        private Dictionary<string, RUDPConnectionData> _sequences { get; set; }
        private bool _isAlive = false;
        private int _maxMTU { get { return (int)(MTU * 0.80); } }
        private object _debugMutex = new object();
        private Thread _thRecv;
        private Thread _thKeepAlive;

        public RUDPConnection()
        {
            IsServer = false;
            MTU = 500;
            RecvFrequencyMs = 10;
            PacketIdLimit = int.MaxValue / 2;
            SequenceLimit = int.MaxValue / 2;
            ConnectionTimeout = 10;
            ClientStartSequence = 100;
            ServerStartSequence = 200;
            State = ConnectionState.CLOSED;
            PacketHeader = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }; // DeadBeef
            InternalACKHeader = new byte[] { 0xFA, 0xCE, 0xFE, 0xED }; // FaceFeed
            InternalKeepAliveHeader = new byte[] { 0xBE, 0xEF, 0xFA, 0xCE }; // BeefFace (lol)
        }

        private void Debug(object obj, params object[] args)
        {
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

        public virtual void Initialize(bool startThreads = true)
        {
            _isAlive = true;
            _sequences = new Dictionary<string, RUDPConnectionData>();
            _clients = new Dictionary<string, IPEndPoint>();
            InitThreads(startThreads);
        }

        public void InitThreads(bool start)
        {
            _thRecv = new Thread(() =>
            {
                while(_isAlive)
                {
                    ProcessRecvQueue();
                    Thread.Sleep(10);
                }
            });
            _thKeepAlive = new Thread(() =>
            {
                while (_isAlive)
                {
                    ProcessKeepAlive();
                    Thread.Sleep(1000);
                }
            });
            if (start)
            {
                _thRecv.Start();
                _thKeepAlive.Start();
            }
        }

        public void Disconnect()
        {
            State = ConnectionState.CLOSING;
            _isAlive = false;
            _socket.Shutdown(SocketShutdown.Both);
            if (IsServer)
                _socket.Close();
            if (_thKeepAlive != null)
                while (_thKeepAlive.IsAlive)
                    Thread.Sleep(10);
            if (_thRecv != null)
                while (_thRecv.IsAlive)
                    Thread.Sleep(10);
            State = ConnectionState.CLOSED;
            OnDisconnected?.Invoke(IsServer ? LocalEndPoint : RemoteEndPoint);
        }

        public override void PacketReceive(IPEndPoint ep, byte[] data, int length)
        {
            base.PacketReceive(ep, data, length);
            DateTime dtNow = DateTime.Now;
            bool isRUDPPacket = length > PacketHeader.Length && data.Take(PacketHeader.Length).SequenceEqual(PacketHeader);
            bool isACKPacket = length > InternalACKHeader.Length && data.Take(InternalACKHeader.Length).SequenceEqual(InternalACKHeader);
            bool isKeepAlivePacket = length == InternalKeepAliveHeader.Length && data.Take(InternalKeepAliveHeader.Length).SequenceEqual(InternalKeepAliveHeader);
            if (isRUDPPacket || isACKPacket || isKeepAlivePacket)
            {
                IPEndPoint src = IsServer ? ep : RemoteEndPoint;
                RUDPConnectionData sq = GetSequence(src);
                sq.LastPacketDate = dtNow;
                if (isRUDPPacket)
                {
                    RUDPPacket p = RUDPPacket.Deserialize(PacketHeader, data);
                    p.Src = src;
                    p.Received = DateTime.Now;
                    Send(p.Src, new RUDPInternalPackets.ACKPacket() { header = InternalACKHeader, sequence = p.Seq }.Serialize());
                    Debug("ACK SEND -> {0}: {1}", p.Src, p.Seq);

                    if (p.Type == RUDPPacketType.RST && p.Flags.HasFlag(RUDPPacketFlags.RST))
                        if (!IsServer)
                        {
                            Debug("GOT RESET BY PEER");
                            Disconnect();
                            return;
                        }

                    lock (sq.ReceivedPackets)
                        sq.ReceivedPackets.Add(p);
                }
                else if(isACKPacket)
                {
                    RUDPInternalPackets.ACKPacket ack = RUDPInternalPackets.ACKPacket.Deserialize(data);
                    Debug("ACK RECV <- {0}: {1}", src, ack.sequence);
                    lock (sq.Pending)
                        foreach (RUDPPacket p in sq.Pending.Where(x => x.Seq == ack.sequence))
                            p.Acknowledged = true;
                }
                else if(isKeepAlivePacket)
                {
                    if (IsServer)
                        sq.State = ConnectionState.OPEN;
                    else
                        State = ConnectionState.OPEN;
                }
            }
            else
                Console.WriteLine("[{0}] RAW RECV: [{1}]", GetType().ToString(), Encoding.ASCII.GetString(data, 0, length));
        }

        private RUDPConnectionData GetSequence(RUDPPacket p)
        {
            return GetSequence(p.Src == null ? p.Dst : p.Src);
        }

        private RUDPConnectionData GetSequence(IPEndPoint ep)
        {
            lock (_sequences)
            {
                if (!_sequences.ContainsKey(ep.ToString()))
                {
                    _sequences[ep.ToString()] = new RUDPConnectionData()
                    {
                        EndPoint = ep,
                        IsNewSequence = true,
                        Local = IsServer ? ServerStartSequence : ClientStartSequence,
                        Remote = IsServer ? ClientStartSequence : ServerStartSequence
                    };
                    while (!_sequences.ContainsKey(ep.ToString()))
                        Thread.Sleep(10);
                    Debug("NEW SEQUENCE: {0}", _sequences[ep.ToString()]);
                    return _sequences[ep.ToString()];
                }
                else
                    return _sequences[ep.ToString()];
            }
        }

        public void Send(string data)
        {
            Send(RemoteEndPoint, RUDPPacketType.DAT, RUDPPacketFlags.NUL, Encoding.ASCII.GetBytes(data));
        }

        public void Send(IPEndPoint destination, RUDPPacketType type = RUDPPacketType.DAT, RUDPPacketFlags flags = RUDPPacketFlags.NUL, byte[] data = null, int[] intData = null)
        {
            bool reset = false;
            RUDPConnectionData sq = GetSequence(destination);
            if ((data != null && data.Length < _maxMTU) || data == null)
            {
                SendPacket(new RUDPPacket()
                {
                    Dst = destination,
                    Id = sq.PacketId,
                    Type = type,
                    Flags = flags,
                    Data = data,
                    intData = intData
                });
                sq.PacketId++;
                if (!IsServer && sq.Local > SequenceLimit)
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
                        Id = sq.PacketId,
                        Type = type,
                        Flags = flags,
                        Data = buf
                    });
                    i += _maxMTU;
                }
                foreach (RUDPPacket p in PacketsToSend)
                {
                    p.Qty = PacketsToSend.Count;
                    SendPacket(p);
                }
                sq.PacketId++;
                if (!IsServer && sq.Local > SequenceLimit)
                    reset = true;
            }
            else
                throw new Exception("This should not happen");
            Retransmit(sq);
            if (sq.PacketId > PacketIdLimit)
                sq.PacketId = 0;
            if (reset)
            {
                SendPacket(new RUDPPacket()
                {
                    Id = sq.PacketId,
                    Dst = destination,
                    Type = RUDPPacketType.RST
                });
                sq.Local = IsServer ? ServerStartSequence : ClientStartSequence;
            }
        }

        private void Retransmit(RUDPConnectionData sq)
        {
            DateTime dtNow = DateTime.Now;
            List<RUDPPacket> retry = new List<RUDPPacket>();
            lock (sq.Pending)
                foreach (RUDPPacket unsent in sq.Pending.Where(x => !x.Acknowledged && (dtNow - x.Sent).Seconds >= 1))
                    retry.Add(unsent);
            foreach(RUDPPacket p in retry)
            {
                p.Retransmit = true;
                SendPacket(p);
            }
        }

        private void SendPacket(RUDPPacket p)
        {
            DateTime dtNow = DateTime.Now;
            RUDPConnectionData sq = GetSequence(p.Dst);

            if (!p.Retransmit)
            {
                p.Seq = sq.Local;
                sq.Local++;
                p.Sent = dtNow;
                Debug("SEND -> {0}: {1}", p.Dst, p);
            }
            else
                Debug("RETRANSMIT -> {0}: {1}", p.Dst, p);

            lock(sq.Pending)
            {
                sq.Pending.RemoveAll(x => x.Dst.ToString() == p.Dst.ToString() && x.Seq == p.Seq);
                sq.Pending.Add(p);
            }

            Send(p.Dst, p.ToByteArray(PacketHeader));

            if(p.Type == RUDPPacketType.RST && p.Flags.HasFlag(RUDPPacketFlags.RST))
                lock(_sequences)
                    _sequences.Remove(p.Dst.ToString());
        }

        public void ProcessKeepAlive()
        {
            DateTime dtNow = DateTime.Now;
            List<IPEndPoint> disconnectSequences = new List<IPEndPoint>();
            foreach (var kvp in _sequences)
            {
                RUDPConnectionData sq = GetSequence(kvp.Value.EndPoint);
                if ((dtNow - sq.LastPacketDate).Seconds > (ConnectionTimeout / 5))
                {
                    Debug("KEEPALIVE -> {0}", sq.EndPoint);
                    Send(sq.EndPoint, new RUDPInternalPackets.KeepAlivePacket() { header = InternalKeepAliveHeader }.Serialize());
                    Retransmit(sq);
                }
                if ((dtNow - sq.LastPacketDate).Seconds > ConnectionTimeout)
                {
                    Debug("TIMEOUT -> {0}", sq.EndPoint);
                    if (IsServer)
                        disconnectSequences.Add(sq.EndPoint);
                    else
                        Disconnect();
                }
            }
            foreach (IPEndPoint ep in disconnectSequences)
            {
                OnClientDisconnect?.Invoke(ep);
                lock (_sequences)
                    _sequences.Remove(ep.ToString());
            }
        }

        public void ProcessRecvQueue()
        {
            foreach(var cdata in _sequences)
            {
                RUDPConnectionData sq = cdata.Value;

                List<RUDPPacket> PacketsToRecv = new List<RUDPPacket>();
                lock (sq.ReceivedPackets)
                    PacketsToRecv.AddRange(sq.ReceivedPackets.OrderBy(x => x.Seq));
                PacketsToRecv = PacketsToRecv.GroupBy(x => x.Seq).Select(g => g.First()).ToList();

                foreach(RUDPPacket p in PacketsToRecv)
                {
                    lock (sq.ReceivedPackets)
                        sq.ReceivedPackets.Remove(p);

                    if (p.Processed)
                        continue;

                    if(sq.IsNewSequence)
                    {
                        if(IsServer)
                        {
                            if(p.Type != RUDPPacketType.SYN)
                            {
                                Send(p.Src, RUDPPacketType.RST, RUDPPacketFlags.RST);
                                break;
                            }
                        }
                        sq.IsNewSequence = false;
                    }

                    if (p.Seq < sq.Remote)
                    {
                        sq.ReceivedPackets.Add(p);
                        continue;
                    }

                    if (p.Seq > sq.Remote)
                    {
                        sq.ReceivedPackets.Add(p);
                        break;
                    }                        

                    Debug("RECV <- {0}: {1}", p.Src, p);

                    if (p.Qty == 0)
                    {
                        sq.Remote++;
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

                        if(p.Type == RUDPPacketType.RST)
                        {
                            sq.Remote = IsServer ? ClientStartSequence : ServerStartSequence;
                            continue;
                        }

                        if(p.Type == RUDPPacketType.ACK)
                            lock(sq.Pending)
                                sq.Pending.RemoveAll(x => p.intData.Contains(x.Id));

                        if(p.Type == RUDPPacketType.DAT)
                            OnPacketReceived?.Invoke(p);
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

                            sq.Remote += p.Qty;
                        }
                        else
                        {
                            if (multiPackets.Count < p.Qty)
                            {
                                sq.ReceivedPackets.Add(p);
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
                if(PacketsToRecv.Where(x => x.Processed).Count() > 0)
                    Send(sq.EndPoint, RUDPPacketType.ACK, RUDPPacketFlags.NUL, null, PacketsToRecv.Where(x => x.Processed).Select(x => x.Id).ToArray());
            }
        }        
    }
}
