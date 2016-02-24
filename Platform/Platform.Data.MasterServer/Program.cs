﻿using System;
using System.IO;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Platform.Communication.Protocol.Udp;
using Platform.Data.Core.Pairs;
using Platform.Data.Core.Sequences;
using Platform.Helpers;

namespace Platform.Data.MasterServer
{
    public static class Program
    {
        private const string DefaultDatabaseFilename = "db.links";

        private static bool LinksServerRunning = true;
        private static UnicodeMap UnicodeMap;

        public static void Main(string[] args)
        {
            Console.CancelKeyPress += OnCancelKeyPressed;

            try
            {
#if DEBUG
                File.Delete(DefaultDatabaseFilename);
#endif

                using (var memoryManager = new LinksMemoryManager(DefaultDatabaseFilename, 8 * 1024 * 1024))
                using (var links = new Links(memoryManager))
                {
                    UnicodeMap = new UnicodeMap(links);
                    UnicodeMap.Init();

                    var sequences = new Sequences(links);

                    PrintContents(links, sequences);

                    Console.WriteLine("Links server started.");
                    Console.WriteLine("Press CTRL+C or ESC to stop server.");

                    using (var sender = new UdpSender(8888))
                    {
                        MessageHandlerCallback handleMessage = message =>
                        {
                            if (!string.IsNullOrWhiteSpace(message))
                            {
                                message = message.Trim();

                                Console.WriteLine("<- {0}", message);

                                if (IsSearch(message))
                                    sequences.Search(sender, ProcessSequenceForSearch(message));
                                else
                                    sequences.Create(sender, ProcessSequenceForCreate(message));
                            }
                        };

                        //using (var receiver = new UdpReceiver(7777, handleMessage))
                        using (var receiver = new UdpClient(7777))
                        {
                            while (LinksServerRunning)
                            {
                                while (receiver.Available > 0)
                                    handleMessage(receiver.ReceiveString());

#if NET45
                                while (Console.KeyAvailable)
                                {
                                    var info = Console.ReadKey(true);
                                    if (info.Key == ConsoleKey.Escape)
                                        LinksServerRunning = false;
                                }
#endif

                                Thread.Sleep(1);
                            }

                            Console.WriteLine("Links server stopped.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Write(ex.ToRecursiveString());
            }

            Console.CancelKeyPress -= OnCancelKeyPressed;
        }

        private static void OnCancelKeyPressed(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            LinksServerRunning = false;
        }

        private static void PrintContents(Links links, Sequences sequences)
        {
            if (links.Count() == UnicodeMap.LastCharLink)
                Console.WriteLine("Database is empty.");
            else
            {
                Console.WriteLine("Contents:");

                var linksTotalLength = links.Count().ToString("0").Length;

                var printFormatBase = new String('0', linksTotalLength);

                // Выделить код по печати одной связи в Extensions

                var printFormat = string.Format("\t[{{0:{0}}}]: {{1:{0}}} -> {{2:{0}}} {{3}}", printFormatBase);

                for (var link = UnicodeMap.LastCharLink + 1; link <= links.Count(); link++)
                {
                    Console.WriteLine(printFormat, link, links.GetSource(link), links.GetTarget(link),
                        sequences.FormatSequence(link, AppendLinkToString, true));
                }
            }
        }

        private static void AppendLinkToString(StringBuilder sb, ulong link)
        {
            if (link <= (char.MaxValue + 1))
                sb.Append(UnicodeMap.FromLinkToChar(link));
            else
                sb.AppendFormat("({0})", link);
        }

        private static bool IsSearch(string message)
        {
            var i = message.Length - 1;
            if (message[i] == '?')
            {
                var escape = 0;
                while (--i >= 0 && IsEscape(message[i]))
                    escape++;
                return escape % 2 == 0;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsEscape(char c)
        {
            return c == '\\' || c == '~';
        }

        private static ulong[] ProcessSequenceForSearch(string sequence)
        {
            return ProcessSequence(sequence, forSearch: true);
        }

        private static ulong[] ProcessSequenceForCreate(string sequence)
        {
            return ProcessSequence(sequence, forSearch: false);
        }

        private static ulong[] ProcessSequence(string sequence, bool forSearch = false)
        {
            var sequenceLength = sequence.Length;
            if (forSearch) sequenceLength--;
            var w = 0;
            for (var r = 0; r < sequenceLength; r++)
            {
                if (IsEscape(sequence[r])) // Last Escape Symbol Ignored
                {
                    if ((r + 1) < sequenceLength)
                    {
                        w++;
                        r++;
                    }
                }
                else
                    w++;
            }

            var result = new ulong[w];

            w = 0;
            if (forSearch)
            {
                for (var r = 0; r < sequenceLength; r++)
                {
                    if (IsEscape(sequence[r])) // Last Escape Symbol Ignored
                    {
                        if ((r + 1) < sequenceLength)
                        {
                            result[w++] = UnicodeMap.FromCharToLink(sequence[r + 1]);
                            r++;
                        }
                    }
                    else if (sequence[r] == '_')
                        result[w++] = LinksConstants.Any;
                    else if (sequence[r] == '*')
                        result[w++] = Sequences.ZeroOrMany;
                    else
                        result[w++] = UnicodeMap.FromCharToLink(sequence[r]);
                }
            }
            else
            {
                for (var r = 0; r < sequence.Length; r++)
                {
                    if (IsEscape(sequence[r])) // Last Escape Symbol Ignored
                    {
                        if ((r + 1) < sequence.Length)
                        {
                            result[w++] = UnicodeMap.FromCharToLink(sequence[r + 1]);
                            r++;
                        }
                    }
                    else
                        result[w++] = UnicodeMap.FromCharToLink(sequence[r]);
                }
            }

            return result;
        }

        private static void Create(this Sequences sequences, UdpSender sender, ulong[] sequence)
        {
            var link = sequences.Create(sequence);

            sender.Send(string.Format("Sequence with balanced variant at {0} created.", link));
        }

        private static void Search(this Sequences sequences, UdpSender sender, ulong[] sequence)
        {
            var containsAny = Array.IndexOf(sequence, LinksConstants.Any) >= 0;
            var containsZeroOrMany = Array.IndexOf(sequence, Sequences.ZeroOrMany) >= 0;

            if (containsZeroOrMany)
            {
                var patternMatched = sequences.MatchPattern(sequence);

                sender.Send(string.Format("{0} sequences matched pattern.", patternMatched.Count));
                foreach (var result in patternMatched)
                    sender.Send(string.Format("\t{0}: {1}", result, sequences.FormatSequence(result, AppendLinkToString, false)));
            }
            if (!containsZeroOrMany)
            {
                var fullyMatched = sequences.Each(sequence);

                sender.Send(string.Format("{0} sequences matched fully.", fullyMatched.Count));
                foreach (var result in fullyMatched)
                    sender.Send(string.Format("\t{0}: {1}", result,
                        sequences.FormatSequence(result, AppendLinkToString, false)));
            }
            if (!containsAny && !containsZeroOrMany)
            {
                var partiallyMatched = sequences.GetAllPartiallyMatchingSequences1(sequence);

                sender.Send(string.Format("{0} sequences matched partially.", partiallyMatched.Count));
                foreach (var result in partiallyMatched)
                    sender.Send(string.Format("\t{0}: {1}", result,
                        sequences.FormatSequence(result, AppendLinkToString, false)));

                var allConnections = sequences.GetAllConnections(sequence);

                sender.Send(string.Format("{0} sequences connects query elements.", allConnections.Count));
                foreach (var result in allConnections)
                    sender.Send(string.Format("\t{0}: {1}", result,
                        sequences.FormatSequence(result, AppendLinkToString, false)));
            }
        }
    }
}