using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TCPServer
{
    /// <summary>
    /// Стан Circuit Breaker
    /// </summary>
    public enum CircuitState
    {
        Closed,      // Нормальна робота, всі запити обробляються
        Open,        // Критичний поріг помилок досягнуто, запити блокуються
        HalfOpen     // Переходний стан для перевірки відновлення
    }

    /// <summary>
    /// Потокобезпечна реалізація patternу Circuit Breaker
    /// для обробки мережевих помилок
    /// </summary>
    public class CircuitBreaker
    {
        private CircuitState _state = CircuitState.Closed;
        private int _failureCount = 0;
        private int _successCount = 0;
        private DateTime _lastFailureTime = DateTime.MinValue;
        private readonly object _lockObject = new object();

        // Налаштування
        private const int FailureThreshold = 5;           // Кількість помилок до відкриття
        private const int SuccessThreshold = 3;           // Кількість успіхів для закриття
        private const int TimeoutSeconds = 30;            // Час перебування у стані Open

        public CircuitState State
        {
            get
            {
                lock (_lockObject)
                {
                    // Якщо Circuit Open, перевіряємо чи пройшло достатньо часу
                    if (_state == CircuitState.Open &&
                        DateTime.UtcNow - _lastFailureTime > TimeSpan.FromSeconds(TimeoutSeconds))
                    {
                        _state = CircuitState.HalfOpen;
                        _successCount = 0;
                        Console.WriteLine("[CB] Перехід у стан HalfOpen (спроба відновлення)");
                    }
                    return _state;
                }
            }
        }

        /// <summary>
        /// Реєстрація успішної операції
        /// </summary>
        public void RecordSuccess()
        {
            lock (_lockObject)
            {
                _failureCount = 0;

                if (_state == CircuitState.HalfOpen)
                {
                    _successCount++;
                    Console.WriteLine($"[CB] Успіх у HalfOpen: {_successCount}/{SuccessThreshold}");

                    if (_successCount >= SuccessThreshold)
                    {
                        _state = CircuitState.Closed;
                        Console.WriteLine("[CB] Перехід у стан Closed (сервіс відновлено)");
                    }
                }
            }
        }

        /// <summary>
        /// Реєстрація помилки операції
        /// </summary>
        public void RecordFailure()
        {
            lock (_lockObject)
            {
                _failureCount++;
                _lastFailureTime = DateTime.UtcNow;
                _successCount = 0;

                Console.WriteLine($"[CB] Помилка зареєстрована: {_failureCount}/{FailureThreshold}");

                if (_failureCount >= FailureThreshold && _state != CircuitState.Open)
                {
                    _state = CircuitState.Open;
                    Console.WriteLine("[CB] ⚠️  КРИТИЧНИЙ поріг помилок! Circuit переведено в стан Open.");
                }
            }
        }

        /// <summary>
        /// Перевірка чи є операція дозволена
        /// </summary>
        public bool IsOperationAllowed()
        {
            return State != CircuitState.Open;
        }
    }

    class Program
    {
        // Глобальний об'єкт Circuit Breaker для всього сервера
        private static CircuitBreaker _circuitBreaker = new CircuitBreaker();

        // Timeout для читання даних від клієнта (5 секунд за умовою)
        private const int ClientReadTimeoutMs = 5000;

        static async Task Main(string[] args)
        {
            // Налаштовуємо кінцеву точку: IP-адреса (локальна) та порт (8000)
            IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 8000);

            // Створюємо асинхронний TCP listener
            TcpListener tcpListener = new TcpListener(ipEndPoint);

            try
            {
                tcpListener.Start(10); // 10 - максимальна черга підключень
                Console.WriteLine("🚀 Асинхронний TCP Сервер запущено на 127.0.0.1:8000");
                Console.WriteLine("   Очікування підключення...\n");

                // Асинхронний цикл для прийняття клієнтів
                while (true)
                {
                    // Перевіряємо стан Circuit Breaker перед прийняттям нового клієнта
                    if (!_circuitBreaker.IsOperationAllowed())
                    {
                        Console.WriteLine("\n⛔ Circuit Breaker у стані Open - нові підключення тимчасово блокуються!");
                        await Task.Delay(1000); // Чекаємо перед наступною спробою
                        continue;
                    }

                    // AcceptTcpClientAsync() - асинхронно чекаємо клієнта без блокування потоку
                    TcpClient tcpClient = await tcpListener.AcceptTcpClientAsync();

                    // Отримуємо інформацію про клієнта
                    IPEndPoint remote = (IPEndPoint)tcpClient.Client.RemoteEndPoint;
                    Console.WriteLine($"\n✅ Клієнт підключився: {remote.Address}:{remote.Port}");

                    // Запускаємо асинхронну обробку клієнта в окремій задачі
                    // Не чекаємо завершення, щоб сервер міг приймати інших клієнтів
                    _ = HandleClientAsync(tcpClient);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Помилка сервера: {ex.Message}");
            }
            finally
            {
                tcpListener.Stop();
                Console.WriteLine("Сервер зупинено.");
            }
        }

        /// <summary>
        /// Асинхронна обробка окремого клієнта
        /// </summary>
        private static async Task HandleClientAsync(TcpClient tcpClient)
        {
            try
            {
                using (tcpClient)
                using (NetworkStream networkStream = tcpClient.GetStream())
                {
                    // Налаштовуємо timeout для потоку (5 секунд)
                    networkStream.ReadTimeout = ClientReadTimeoutMs;

                    byte[] bufReceive = new byte[1024];

                    try
                    {
                        // Асинхронно читаємо дані від клієнта з таймаутом
                        // Використовуємо CancellationToken для реалізації timeout
                        using (var cts = new CancellationTokenSource(ClientReadTimeoutMs))
                        {
                            int bytesRead = await networkStream.ReadAsync(bufReceive, 0, bufReceive.Length, cts.Token);

                            if (bytesRead == 0)
                            {
                                Console.WriteLine("   ⚠️  Клієнт закрив з'єднання без надсилання даних");
                                _circuitBreaker.RecordFailure();
                                return;
                            }

                            // Перетворюємо байти назад у текст
                            string receivedMessage = Encoding.UTF8.GetString(bufReceive, 0, bytesRead);
                            Console.WriteLine($"   📨 Отримано: {receivedMessage}");

                            // Рахуємо кількість літер 'a' (англійська) та 'а' (українська)
                            int countA = receivedMessage.Count(c => c == 'a' || c == 'A' || c == 'а' || c == 'А');

                            // Формуємо відповідь
                            string responseMessage = $"Кількість літер 'а' у вашому повідомленні: {countA}";

                            // Асинхронно відправляємо відповідь клієнту
                            byte[] responseData = Encoding.UTF8.GetBytes(responseMessage);
                            await networkStream.WriteAsync(responseData, 0, responseData.Length);
                            await networkStream.FlushAsync();

                            Console.WriteLine($"   📤 Відповідь відправлено: {responseMessage}");

                            // Записуємо успіх у Circuit Breaker
                            _circuitBreaker.RecordSuccess();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Console.WriteLine("   ⏱️  ТАЙМАУТ: Клієнт не відправив дані протягом 5 секунд");
                        _circuitBreaker.RecordFailure();
                    }
                    catch (IOException ioEx)
                    {
                        Console.WriteLine($"   ❌ Помилка мережі: {ioEx.Message}");
                        _circuitBreaker.RecordFailure();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Помилка обробки клієнта: {ex.Message}");
                _circuitBreaker.RecordFailure();
            }
            finally
            {
                Console.WriteLine("   ✋ З'єднання з клієнтом закрито\n");
            }
        }
    }
}