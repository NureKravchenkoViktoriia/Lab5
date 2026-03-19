using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TCPServer
{
    class Program
    {
        static void Main(string[] args)
        {
            // Налаштовуємо кінцеву точку: IP-адреса (локальна) та порт (8000) 
            IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 8000);

            // Створюємо сокет сервера для роботи по протоколу TCP 
            Socket serverSock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            // Зв'язуємо сокет з нашою кінцевою точкою 
            serverSock.Bind(ipEndPoint);

            // Переводимо сокет у режим прослуховування. 10 - максимальна черга підключень 
            serverSock.Listen(10);
            Console.WriteLine("TCP Сервер запущено. Очікування підключення...");

            // Безкінечний цикл, щоб сервер міг обробляти клієнтів одного за іншим
            while (true)
            {
                // Accept() зупиняє програму і чекає, поки не підключиться клієнт 
                Socket clientSock = serverSock.Accept();
                IPEndPoint remote = (IPEndPoint)clientSock.RemoteEndPoint;
                Console.WriteLine($"\nКлієнт підключився: {remote.Address}:{remote.Port} [cite: 251, 252, 253]");

                // Підготовлюємо буфер на 1024 байти 
                byte[] bufReceive = new byte[1024];

                // Отримуємо дані від клієнта 
                int bytesRead = clientSock.Receive(bufReceive);

                // Перетворюємо байти назад у текст 
                string receivedMessage = Encoding.UTF8.GetString(bufReceive, 0, bytesRead);
                Console.WriteLine($"Отримано повідомлення: {receivedMessage}");

                // Рахуємо кількість літер 'a' (англійська) та 'а' (українська)
                int countA = receivedMessage.Count(c => c == 'a' || c == 'A' || c == 'а' || c == 'А');

                // Формуємо відповідь
                string responseMessage = $"Кількість літер 'а' у вашому повідомленні: {countA}";

                // Перетворюємо відповідь у байти та відправляємо клієнту 
                byte[] responseData = Encoding.UTF8.GetBytes(responseMessage);
                clientSock.Send(responseData);

                // Закриваємо з'єднання з цим конкретним клієнтом 
                clientSock.Close();
                Console.WriteLine("Відповідь відправлено. З'єднання закрито.");
            }
        }
    }
}