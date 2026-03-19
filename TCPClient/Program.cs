using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TCPClient
{
    class Program
    {
        static void Main(string[] args)
        {
            // Створюємо клієнтський сокет
            Socket cliSock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            // Вказуємо адресу сервера, до якого хочемо підключитися
            IPAddress ipServ = IPAddress.Parse("127.0.0.1");
            IPEndPoint ipEndP = new IPEndPoint(ipServ, 8000);

            try
            {
                // Намагаємося встановити з'єднання
                cliSock.Connect(ipEndP);
                Console.WriteLine("Успішне підключення до сервера!");

                // Просимо користувача ввести повідомлення
                Console.Write("Введіть повідомлення для сервера: ");
                string message = Console.ReadLine();

                // Перетворюємо текст у масив байтів
                byte[] pack = Encoding.UTF8.GetBytes(message);

                // Відправляємо дані на сервер
                cliSock.Send(pack);

                // Створюємо буфер для отримання відповіді
                byte[] bufReceive = new byte[1024];

                // Чекаємо відповідь від сервера
                int bytesRead = cliSock.Receive(bufReceive);

                // Перетворюємо отримані байти у текст і виводимо на екран
                string response = Encoding.UTF8.GetString(bufReceive, 0, bytesRead);
                Console.WriteLine($"\nВідповідь від сервера: {response}");
            }
            catch (SocketException ex) // Обробка помилок підключення
            {
                Console.WriteLine($"Помилка з'єднання: {ex.Message}");
            }
            finally
            {
                // Завжди закриваємо сокет після завершення роботи
                cliSock.Close();
                Console.WriteLine("\nДля завершення натисніть будь-яку клавішу...");
                Console.ReadKey();
            }
        }
    }
}