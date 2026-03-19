using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TCPServerAsync
{
    public enum CircuitState { Closed, Open, HalfOpen }

    public class CircuitBreaker
    {
        private CircuitState _state = CircuitState.Closed;
        private int _failureCount = 0;
        private DateTime _lastFailureTime;
        private readonly int _failureThreshold = 5;
        private readonly TimeSpan _openDuration = TimeSpan.FromSeconds(10);
        private readonly object _lock = new object();
        private bool _isTesting = false;

        public bool CanExecute()
        {
            lock (_lock)
            {
                if (_state == CircuitState.Open)
                {
                    if (DateTime.UtcNow - _lastFailureTime > _openDuration)
                    {
                        _state = CircuitState.HalfOpen;
                        _isTesting = true;
                        Console.WriteLine("[CircuitBreaker] Стан змінено на HalfOpen. Дозволено один тестовий запит...");
                        return true;
                    }
                    return false;
                }

                if (_state == CircuitState.HalfOpen)
                {
                    if (_isTesting)
                    {
                        _isTesting = false;
                        return true;
                    }
                    return false;
                }

                return true;
            }
        }

        public void RecordSuccess()
        {
            lock (_lock)
            {
                if (_state == CircuitState.HalfOpen)
                {
                    _state = CircuitState.Closed;
                    _failureCount = 0;
                    Console.WriteLine("[CircuitBreaker] Тестовий запит успішний. Сервер повністю відновлено (Closed).");
                }
                else if (_state == CircuitState.Closed)
                {
                    _failureCount = 0;
                }
            }
        }

        public void RecordFailure()
        {
            lock (_lock)
            {
                _failureCount++;
                _lastFailureTime = DateTime.UtcNow;
                Console.WriteLine($"[CircuitBreaker] Зафіксовано помилку. Загальна кількість: {_failureCount}");

                if (_state == CircuitState.HalfOpen || _failureCount >= _failureThreshold)
                {
                    _state = CircuitState.Open;
                    Console.WriteLine("[CircuitBreaker] Досягнуто ліміт помилок. Стан змінено на OPEN! Блокування нових запитів.");
                }
            }
        }
    }

    class Program
    {
        private static CircuitBreaker _circuitBreaker = new CircuitBreaker();
        private const int TimeoutMilliseconds = 5000;

        static async Task Main(string[] args)
        {
            IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 8000);
            TcpListener tcpListener = new TcpListener(ipEndPoint);

            try
            {
                tcpListener.Start(10);
                Console.WriteLine("Асинхронний TCP Сервер запущено на 127.0.0.1:8000");
                Console.WriteLine("Очікування підключення...");

                while (true)
                {
                    if (!_circuitBreaker.CanExecute())
                    {
                        Console.WriteLine("Circuit Breaker активний (Open) - запит відхилено.");
                        await Task.Delay(1000);
                        continue;
                    }

                    TcpClient tcpClient = await tcpListener.AcceptTcpClientAsync();

                    IPEndPoint remote = (IPEndPoint)tcpClient.Client.RemoteEndPoint;
                    Console.WriteLine($"\nКлієнт підключився: {remote.Address}:{remote.Port}");

                    _ = HandleClientAsync(tcpClient);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Критична помилка сервера: {ex.Message}");
            }
            finally
            {
                tcpListener.Stop();
            }
        }

        private static async Task HandleClientAsync(TcpClient tcpClient)
        {
            try
            {
                using (tcpClient)
                using (NetworkStream networkStream = tcpClient.GetStream())
                using (var cts = new CancellationTokenSource(TimeoutMilliseconds))
                {
                    byte[] bufReceive = new byte[1024];

                    int bytesRead = await networkStream.ReadAsync(bufReceive, 0, bufReceive.Length, cts.Token);

                    if (bytesRead == 0)
                    {
                        Console.WriteLine("Клієнт закрив з'єднання.");
                        return;
                    }

                    string receivedMessage = Encoding.UTF8.GetString(bufReceive, 0, bytesRead);
                    Console.WriteLine($"Отримано: {receivedMessage}");

                    int countA = receivedMessage.Count(c => c == 'a' || c == 'A' || c == 'а' || c == 'А');
                    string responseMessage = $"Кількість літер 'а' у вашому повідомленні: {countA}";

                    byte[] responseData = Encoding.UTF8.GetBytes(responseMessage);
                    await networkStream.WriteAsync(responseData, 0, responseData.Length);
                    await networkStream.FlushAsync();

                    Console.WriteLine("Відповідь успішно відправлено.");

                    _circuitBreaker.RecordSuccess();
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Таймаут: Клієнт не відправив дані протягом 5 секунд.");
                _circuitBreaker.RecordFailure();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка мережі при обробці клієнта: {ex.Message}");
                _circuitBreaker.RecordFailure();
            }
            finally
            {
                Console.WriteLine("З'єднання закрито.");
            }
        }
    }
}