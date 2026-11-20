using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using PokeCreator.Battles;
using PokeCreator.Pkmn;

namespace PokeCreator.Runtime.World
{
    /// <summary>One message between two linked games (one JSON object per line).</summary>
    public class LinkMessage
    {
        public string Type { get; set; }
        public string Name { get; set; }
        public string Game { get; set; }
        public int Seed { get; set; }
        public bool Double { get; set; }
        public List<Pokemon> Party { get; set; }
        public List<List<int>> Commands { get; set; }
        public int Slot { get; set; }
        public int Index { get; set; }
        public int Turn { get; set; }
        public bool Accept { get; set; }
        public string Mode { get; set; }
    }

    /// <summary>
    /// A TCP connection to another copy of the same game (LAN or internet with the port forwarded). The host listens,
    /// the other player joins by address; a background thread reads messages, the game polls them every frame.
    /// </summary>
    public class LinkSession : IDisposable
    {
        public const int DefaultPort = 7341;
        TcpListener _listener;
        TcpClient _client;
        StreamWriter _writer;
        readonly ConcurrentQueue<LinkMessage> _in = new ConcurrentQueue<LinkMessage>();
        volatile bool _connected, _closed;
        public bool IsHost { get; private set; }
        public bool Connected { get { return _connected && !_closed; } }
        public bool Closed { get { return _closed; } }
        public string Error { get; private set; }

        public static LinkSession Host(int port)
        {
            var s = new LinkSession { IsHost = true };
            try
            {
                s._listener = new TcpListener(IPAddress.Any, port);
                s._listener.Start();
                new Thread(() =>
                {
                    try { var c = s._listener.AcceptTcpClient(); s._listener.Stop(); s.Attach(c); }
                    catch (Exception e) { if (!s._closed) { s.Error = e.Message; s._closed = true; } }
                }) { IsBackground = true }.Start();
            }
            catch (Exception e) { s.Error = e.Message; s._closed = true; }
            return s;
        }

        public static LinkSession Join(string host, int port)
        {
            var s = new LinkSession { IsHost = false };
            new Thread(() =>
            {
                try { var c = new TcpClient(); c.Connect(host, port); s.Attach(c); }
                catch (Exception e) { s.Error = e.Message; s._closed = true; }
            }) { IsBackground = true }.Start();
            return s;
        }

        void Attach(TcpClient c)
        {
            _client = c; c.NoDelay = true;
            var stream = c.GetStream();
            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            _connected = true;
            var reader = new StreamReader(stream, Encoding.UTF8);
            new Thread(() =>
            {
                try
                {
                    string line;
                    while (!_closed && (line = reader.ReadLine()) != null)
                    {
                        try { _in.Enqueue(Json.Json.Deserialize<LinkMessage>(line)); } catch { }
                    }
                }
                catch (Exception e) { if (!_closed) Error = e.Message; }
                _closed = true;
            }) { IsBackground = true }.Start();
        }

        public void Send(LinkMessage m)
        {
            if (!Connected) return;
            try { lock (this) _writer.WriteLine(Json.Json.Write(m, false).Replace("\r", "").Replace("\n", " ")); }
            catch (Exception e) { Error = e.Message; _closed = true; }
        }

        public LinkMessage Poll() { LinkMessage m; return _in.TryDequeue(out m) ? m : null; }

        /// <summary>This computer's addresses, to tell the other player.</summary>
        public static List<string> LocalAddresses()
        {
            try { return Dns.GetHostAddresses(Dns.GetHostName()).Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)).Select(a => a.ToString()).ToList(); }
            catch { return new List<string>(); }
        }

        public void Dispose()
        {
            _closed = true;
            try { if (_listener != null) _listener.Stop(); } catch { }
            try { if (_client != null) _client.Close(); } catch { }
        }
    }
}
