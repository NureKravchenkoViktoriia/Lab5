using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TCPServer
{
    // Потокобезпечний Circuit Breaker
    public class CircuitBreaker
    {
        private readonly int _failureThreshold;
        private readonly TimeSpan _breakDuration;
        private int _failureCount;
        private DateTime _lastFailureTime;
        private CircuitState _state = CircuitState.Closed;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public enum CircuitState { Closed, Open, HalfOpen }

        public CircuitState State => _state;

        public CircuitBreaker(int failureThreshold = 5, TimeSpan breakDuration = default)
        {
            _failureThreshold = failureThreshold;
            _breakDuration = breakDuration == default ? TimeSpan.FromSeconds(30) : breakDuration;
        }

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
        {
            await _lock.WaitAsync();
            try
            {
                if (_state == CircuitState.Open && DateTime.UtcNow - _lastFailureTime < _breakDuration)
                {
                    throw new BrokenCircuitException("Circuit breaker is OPEN");
                }
                if (_state == CircuitState.HalfOpen)
                {
                    _state = CircuitState.Closed; // Тестуємо один запит
                }

                T result = await action();
                if (_failureCount > 0) _failureCount--;
                return result;
            }
            catch (Exception)
            {
                _failureCount++;
                _lastFailureTime = DateTime.UtcNow;
                if (_failureCount >= _failureThreshold)
                {
                    _state = CircuitState.Open;
                }
                throw;
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    class Program
    {
        private static CircuitBreaker _circuitBreaker = new CircuitBreaker();

        static async Task Main(string[] args)
        {
            IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 8000);
            using var listener = new TcpListener(ipEndPoint);
            listener.Start(10);
            Console.WriteLine("Асинхронний TCP Сервер запущено. Очікування підключень...");

            while (true)
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleClientAsync(client)); // Обробка кожного клієнта в окремому завданні
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Помилка прийняття клієнта: {ex.Message}");
                }
            }
        }

        static async Task HandleClientAsync(TcpClient client)
        {
            try
            {
                await _circuitBreaker.ExecuteAsync(async () =>
                {
                    var remote = (IPEndPoint)client.Client.RemoteEndPoint;
                    Console.WriteLine($"\nКлієнт підключився: {remote.Address}:{remote.Port}");

                    using var stream = client.GetStream();
                    byte[] bufReceive = new byte[1024];

                    // Таймаут 5 секунд на читання з Task.WhenAny
                    var readTask = stream.ReadAsync(bufReceive, 0, bufReceive.Length);
                    var timeoutTask = Task.Delay(5000);
                    var completedTask = await Task.WhenAny(readTask, timeoutTask);

                    int bytesRead;
                    if (completedTask == timeoutTask)
                    {
                        throw new TimeoutException("Таймаут читання даних від клієнта (5 сек)");
                    }
                    bytesRead = await readTask;

                    string receivedMessage = Encoding.UTF8.GetString(bufReceive, 0, bytesRead);
                    Console.WriteLine($"Отримано: {receivedMessage}");

                    int countA = receivedMessage.Count(c => c == 'a' || c == 'A' || c == 'а' || c == 'А');
                    string responseMessage = $"Кількість літер 'а': {countA}";
                    byte[] responseData = Encoding.UTF8.GetBytes(responseMessage);
                    await stream.WriteAsync(responseData, 0, responseData.Length);

                    Console.WriteLine("Відповідь відправлено.");
                });
            }
            catch (TimeoutException tex)
            {
                Console.WriteLine($"Таймаут клієнта: {tex.Message}");
            }
            catch (BrokenCircuitException bcex)
            {
                Console.WriteLine($"Circuit Breaker відкрито: {bcex.Message}. Стан: {_circuitBreaker.State}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка обробки клієнта: {ex.Message}");
            }
            finally
            {
                client.Close();
                Console.WriteLine("З'єднання закрито.");
            }
        }
    }

    public class BrokenCircuitException : Exception
    {
        public BrokenCircuitException(string message) : base(message) { }
    }
}
