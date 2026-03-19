using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TCPServerAsync
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var server = new AsyncTcpServer(IPAddress.Parse("127.0.0.1"), 8000);
            await server.StartAsync();
        }
    }

    public class AsyncTcpServer
    {
        private readonly TcpListener _listener;
        private readonly CircuitBreaker _circuitBreaker;

        public AsyncTcpServer(IPAddress ip, int port)
        {
            _listener = new TcpListener(ip, port);
            _circuitBreaker = new CircuitBreaker(
                failureThreshold: 5,
                openStateDuration: TimeSpan.FromSeconds(10)
            );
        }

        public async Task StartAsync()
        {
            _listener.Start();
            Console.WriteLine("TCP Сервер (async) запущено...");

            while (true)
            {
                if (!_circuitBreaker.CanExecute())
                {
                    Console.WriteLine("Circuit OPEN — нові клієнти тимчасово не приймаються...");
                    await Task.Delay(1000);
                    continue;
                }

                TcpClient client = await _listener.AcceptTcpClientAsync();
                _ = HandleClientAsync(client); // не блокуємо цикл
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            var remote = client.Client.RemoteEndPoint;
            Console.WriteLine($"\nКлієнт підключився: {remote}");

            try
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 5000;

                    string message = await ReadWithTimeoutAsync(stream, 5000);

                    Console.WriteLine($"Отримано: {message}");

                    int countA = message.Count(c =>
                        c == 'a' || c == 'A' ||
                        c == 'а' || c == 'А');

                    string response = $"Кількість літер 'а': {countA}";
                    byte[] data = Encoding.UTF8.GetBytes(response);

                    await stream.WriteAsync(data, 0, data.Length);

                    Console.WriteLine("Відповідь відправлено.");
                    _circuitBreaker.OnSuccess();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка: {ex.Message}");
                _circuitBreaker.OnFailure();
            }
        }

        private async Task<string> ReadWithTimeoutAsync(NetworkStream stream, int timeoutMs)
        {
            byte[] buffer = new byte[1024];

            using (var cts = new CancellationTokenSource(timeoutMs))
            {
                var readTask = stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);

                int bytesRead = await readTask;

                if (bytesRead == 0)
                    throw new IOException("Клієнт закрив з'єднання");

                return Encoding.UTF8.GetString(buffer, 0, bytesRead);
            }
        }
    }

    // 🔥 Thread-safe Circuit Breaker
    public class CircuitBreaker
    {
        private readonly int _failureThreshold;
        private readonly TimeSpan _openDuration;

        private int _failureCount = 0;
        private DateTime _lastFailureTime = DateTime.MinValue;

        private readonly object _lock = new object();

        public CircuitBreaker(int failureThreshold, TimeSpan openStateDuration)
        {
            _failureThreshold = failureThreshold;
            _openDuration = openStateDuration;
        }

        public bool CanExecute()
        {
            lock (_lock)
            {
                if (_failureCount >= _failureThreshold)
                {
                    if ((DateTime.Now - _lastFailureTime) > _openDuration)
                    {
                        Console.WriteLine("Circuit HALF-OPEN → пробуємо знову...");
                        _failureCount = 0;
                        return true;
                    }

                    return false;
                }

                return true;
            }
        }

        public void OnSuccess()
        {
            lock (_lock)
            {
                _failureCount = 0;
            }
        }

        public void OnFailure()
        {
            lock (_lock)
            {
                _failureCount++;
                _lastFailureTime = DateTime.Now;

                Console.WriteLine($"Failure count: {_failureCount}");

                if (_failureCount >= _failureThreshold)
                {
                    Console.WriteLine("Circuit OPEN!");
                }
            }
        }
    }
}