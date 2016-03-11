using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using Platform.Communication.Protocol.Udp;
using Platform.Data.Core.Pairs;
using Platform.Data.Core.Sequences;
using Platform.Helpers;
using Platform.Web.Crawler;

namespace Platform.Data.CrawlerServer
{
    internal static class Program
    {
        private const string DefaultDatabaseFilename = "db.links";

        public static ConcurrentBag<Task> Tasks = new ConcurrentBag<Task>();

        private static void Main()
        {
            new LogService().Configure();

            var cancellationSource = ConsoleHelpers.HandleCancellation();

            var logger = LogManager.GetLogger("default");

            try
            {
                using (var memoryManager = new LinksMemoryManager(DefaultDatabaseFilename, 8 * 1024 * 1024))
                using (var links = new Links(memoryManager))
                {
                    new UnicodeMap(links).Init();

                    ulong pageMarker;
                    if (links.Exists(UnicodeMap.MapSize + 1))
                        pageMarker = UnicodeMap.MapSize + 1;
                    else
                        pageMarker = links.Create(LinksConstants.Itself, LinksConstants.Itself);

                    ulong sequencesMarker;
                    if (links.Exists(UnicodeMap.MapSize + 2))
                        sequencesMarker = UnicodeMap.MapSize + 2;
                    else
                        sequencesMarker = links.Create(LinksConstants.Itself, LinksConstants.Itself);

                    var sequencesOptions = new SequencesOptions();
                    sequencesOptions.UseCompression = true;
                    sequencesOptions.UseSequenceMarker = true;
                    sequencesOptions.SequenceMarkerLink = sequencesMarker;

                    var sequences = new Sequences(links, sequencesOptions);

                    var pagesRepository = new PagesService(links, sequences, pageMarker);

                    Console.WriteLine("Сервер запущен.");
                    Console.WriteLine("Нажмите CTRL+C или ESC чтобы остановить.");

                    using (var sender = new UdpSender(8888))
                    {
                        MessageHandlerCallback handleMessage = message =>
                        {
                            if (!string.IsNullOrWhiteSpace(message))
                            {
                                message = message.Trim();

                                Console.WriteLine("<- {0}", message);

                                Uri siteUri;
                                if (Uri.TryCreate(message, UriKind.Absolute, out siteUri))
                                {
                                    Tasks.Add(Task.Factory.StartNew(() =>
                                        new Crawler(pagesRepository, cancellationSource.Token).Start(siteUri)));

                                    sender.Send(string.Format("Сайт {0} добавлен в очередь на обработку.", siteUri));
                                }
                                else
                                {
                                    Tasks.Add(Task.Factory.StartNew(() =>
                                        {
                                            pagesRepository.SearchPages(message, page =>
                                            {
                                                var uri = pagesRepository.LoadPageUriOrNull(page);
                                                if (uri != null)
                                                    sender.Send(string.Format("\t{0}: {1}", page, uri.ToString()));

                                                return true;
                                            });
                                        }));
                                }
                            }
                        };

                        //using (var receiver = new UdpReceiver(7777, handleMessage))
                        using (var receiver = new UdpClient(7777))
                        {
                            while (!cancellationSource.IsCancellationRequested)
                            {
                                while (receiver.Available > 0)
                                    handleMessage(receiver.ReceiveString());

                                while (Console.KeyAvailable)
                                {
                                    var info = Console.ReadKey(true);
                                    if (info.Key == ConsoleKey.Escape)
                                        cancellationSource.Cancel();
                                }

                                Thread.Sleep(1);
                            }

                            Console.WriteLine("Ожидаем завершения процессов...");

                            Tasks.ToList().ForEach(x => x.Wait());

                            Console.WriteLine("Сервер остановлен.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex.Message, ex);
            }

            ConsoleHelpers.PressAnyKeyToContinue();
        }
    }
}
