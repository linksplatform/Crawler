using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using Platform.Data;
using Platform.Data.Doublets;
using Platform.Data.Doublets.Decorators;
using Platform.Data.Doublets.Memory;
using Platform.Data.Doublets.Memory.United.Specific;
using Platform.Data.Doublets.Sequences;
using Platform.Data.Doublets.Unicode;
using Platform.Memory;
using Platform.Threading;
using EdgeFunc = System.Func<object, System.Threading.Tasks.Task<object>>;

#pragma warning disable 1998

// ReSharper disable ForCanBeConvertedToForeach
// ReSharper disable RedundantLambdaParameterType

namespace Platform.Web.Crawler
{
    /// <summary>
    /// <para>
    /// Represents the edge js proxy.
    /// </para>
    /// <para></para>
    /// </summary>
    public class EdgeJsProxy
    {
        /// <summary>
        /// <para>
        /// The default data file.
        /// </para>
        /// <para></para>
        /// </summary>
        private const string DefaultDataFile = "db.links";

        /// <summary>
        /// <para>
        /// The tasks.
        /// </para>
        /// <para></para>
        /// </summary>
        private readonly ConcurrentBag<Task> _tasks;
        /// <summary>
        /// <para>
        /// The logger.
        /// </para>
        /// <para></para>
        /// </summary>
        private ILog _logger;
        /// <summary>
        /// <para>
        /// The cancellation source.
        /// </para>
        /// <para></para>
        /// </summary>
        private CancellationTokenSource _cancellationSource;

        /// <summary>
        /// <para>
        /// The data path.
        /// </para>
        /// <para></para>
        /// </summary>
        private string _dataPath;

        /// <summary>
        /// <para>
        /// The memory manager.
        /// </para>
        /// <para></para>
        /// </summary>
        private UInt64UnitedMemoryLinks _memoryManager;
        /// <summary>
        /// <para>
        /// The links.
        /// </para>
        /// <para></para>
        /// </summary>
        private UInt64Links _links;

        /// <summary>
        /// <para>
        /// The page marker.
        /// </para>
        /// <para></para>
        /// </summary>
        private ulong _pageMarker;
        /// <summary>
        /// <para>
        /// The sequences marker.
        /// </para>
        /// <para></para>
        /// </summary>
        private ulong _sequencesMarker;

        /// <summary>
        /// <para>
        /// The disposed.
        /// </para>
        /// <para></para>
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// <para>
        /// Initializes a new <see cref="EdgeJsProxy"/> instance.
        /// </para>
        /// <para></para>
        /// </summary>
        public EdgeJsProxy()
        {
            _tasks = new ConcurrentBag<Task>();

            _disposed = false;
        }

        /// <summary>
        /// <para>
        /// Allocates the marker using the specified links.
        /// </para>
        /// <para></para>
        /// </summary>
        /// <param name="links">
        /// <para>The links.</para>
        /// <para></para>
        /// </param>
        /// <param name="currentMarker">
        /// <para>The current marker.</para>
        /// <para></para>
        /// </param>
        /// <param name="marker">
        /// <para>The marker.</para>
        /// <para></para>
        /// </param>
        /// <param name="markerName">
        /// <para>The marker name.</para>
        /// <para></para>
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// <para></para>
        /// <para></para>
        /// </exception>
        private static void AllocateMarker(ILinks<ulong> links, ref ulong currentMarker, out ulong marker, string markerName)
        {
            marker = links.Exists(currentMarker) ? currentMarker : links.CreatePoint();
            if (marker != currentMarker)
            {
                throw new InvalidOperationException(string.Format("Не удалось создать {0} по ожидаемому адресу {1}", markerName, currentMarker));
            }
            currentMarker++;
        }

        /// <summary>
        /// <para>
        /// Allocates the markers using the specified links.
        /// </para>
        /// <para></para>
        /// </summary>
        /// <param name="links">
        /// <para>The links.</para>
        /// <para></para>
        /// </param>
        /// <param name="pageMarker">
        /// <para>The page marker.</para>
        /// <para></para>
        /// </param>
        /// <param name="sequencesMarker">
        /// <para>The sequences marker.</para>
        /// <para></para>
        /// </param>
        private static void AllocateMarkers(ILinks<ulong> links, out ulong pageMarker, out ulong sequencesMarker)
        {
            ulong currentMarker = UnicodeMap.MapSize + 1;

            AllocateMarker(links, ref currentMarker, out pageMarker, "маркер страницы");
            AllocateMarker(links, ref currentMarker, out sequencesMarker, "маркер последовательности");
        }

        /// <summary>
        /// <para>
        /// Waits the all.
        /// </para>
        /// <para></para>
        /// </summary>
        private async Task WaitAll()
        {
            Task task;
            while (_tasks.TryTake(out task))
                await task;
        }

        /// <summary>
        /// <para>
        /// Stops the all.
        /// </para>
        /// <para></para>
        /// </summary>
        /// <returns>
        /// <para>A task containing the object</para>
        /// <para></para>
        /// </returns>
        private async Task<object> StopAll()
        {
            EnsureNotDisposed();

            _cancellationSource.Cancel();

            await WaitAll();

            _cancellationSource = new CancellationTokenSource();

            return true;
        }

        /// <summary>
        /// <para>
        /// Disposes the all.
        /// </para>
        /// <para></para>
        /// </summary>
        private async Task DisposeAll()
        {
            _cancellationSource.Cancel();

            await WaitAll();

            _links.Dispose();
            _memoryManager.Dispose();

            _disposed = true;
        }

        /// <summary>
        /// <para>
        /// Ensures the not disposed.
        /// </para>
        /// <para></para>
        /// </summary>
        /// <exception cref="ObjectDisposedException">
        /// <para>EdgeJsProxy</para>
        /// <para></para>
        /// </exception>
        private void EnsureNotDisposed()
        {
            if (_disposed) throw new ObjectDisposedException("EdgeJsProxy");
        }

        /// <summary>
        /// <para>
        /// Invokes the input.
        /// </para>
        /// <para></para>
        /// </summary>
        /// <param name="input">
        /// <para>The input.</para>
        /// <para></para>
        /// </param>
        /// <returns>
        /// <para>A task containing the object</para>
        /// <para></para>
        /// </returns>
        public async Task<object> Invoke(dynamic input)
        {
            EnsureNotDisposed();

            try
            {
                var logPath = input.logPath as string;

                Console.OutputEncoding = Encoding.UTF8;

                Console.WriteLine(".NET CLR Version: {0}", Environment.Version.ToString());

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

                new LogService().Configure(logPath);
                _logger = LogManager.GetLogger("default");

                _cancellationSource = new CancellationTokenSource();

                _dataPath = input.dataPath as string;

                if (string.IsNullOrWhiteSpace(_dataPath))
                    _dataPath = DefaultDataFile;

                var minimumAllocationBytes = 64 * 1024 * 1024;
                _memoryManager = new UInt64UnitedMemoryLinks(new FileMappedResizableDirectMemory(_dataPath, minimumAllocationBytes), minimumAllocationBytes, new LinksConstants<ulong>(), IndexTreeType.SizedAndThreadedAVLBalancedTree);
                _links = new UInt64Links(_memoryManager);

                new UnicodeMap(_links).Init();

                AllocateMarkers(_links, out _pageMarker, out _sequencesMarker);

                var sequencesOptions = new SequencesOptions<ulong>
                {
                    UseCompression = true,
                    UseSequenceMarker = true,
                    SequenceMarkerLink = _sequencesMarker,
                };

                var sequences = new Sequences(new SynchronizedLinks<ulong>(_links), sequencesOptions);

                var pagesRepository = new PagesService(_links, sequences, _pageMarker);

                var proxy = new
                {
                    StartSearch = (EdgeFunc)(async (dynamic p) =>
                    {
                        EnsureNotDisposed();

                        await WaitAll();

                        var query = (string)p.query;
                        var pageFound = (EdgeFunc)p.pageFound;
                        var searchFinished = (EdgeFunc)p.searchFinished;
                        var pagesCounter = 0;

                        var task = Task.Factory.StartNew(() =>
                        {
                            pagesRepository.SearchPages(query, page =>
                            {
                                if (_cancellationSource.IsCancellationRequested)
                                    return false;

                                var result = pageFound(new
                                {
                                    Id = page,
                                    Url = pagesRepository.LoadPageUriOrNull(page).ToString()
                                }).AwaitResult();

                                pagesCounter++;

                                return !(result is bool) || (bool)result;
                            }, () =>
                            {
                                searchFinished(new
                                {
                                    Query = query,
                                    ResultsCount = pagesCounter
                                }).AwaitResult();
                            });
                        });

                        _tasks.Add(task);

                        return null;
                    }),

                    StopSearch = (EdgeFunc)(async p => await StopAll()),

                    StartCrawl = (EdgeFunc)(async (dynamic p) =>
                    {
                        EnsureNotDisposed();

                        _logger.Info("StartCrawl proxy method executed.");

                        await WaitAll();

                        var urls = ((object[])p.urls).Cast<string>().ToArray();
                        var pageCrawled = (EdgeFunc)p.pageCrawled;

                        var crawler = new Crawler(pagesRepository, _cancellationSource.Token, args => pageCrawled(args));

                        Task task = null;

                        for (var i = 0; i < urls.Length; i++)
                        {
                            Uri uri;
                            if (Uri.TryCreate(urls[i], UriKind.Absolute, out uri))
                            {
                                if (task == null)
                                {
                                    task = Task.Factory.StartNew(() =>
                                    {
                                        _logger.Info("Starting crawling...");
                                        crawler.Start(uri);
                                        _logger.Info("Crawling stopped.");
                                    });
                                    _tasks.Add(task);
                                }
                                else
                                {
                                    task = task.ContinueWith(t =>
                                    {
                                        _logger.Info("Starting crawling...");
                                        crawler.Start(uri);
                                        _logger.Info("Crawling stopped.");
                                    });
                                    _tasks.Add(task);
                                }
                            }
                        }

                        if (task != null) await task;

                        return null;
                    }),

                    StopCrawl = (EdgeFunc)(async p => await StopAll()),

                    Reset = (EdgeFunc)(async p =>
                    {
                        if (!_disposed) await DisposeAll();

                        File.Delete(_dataPath);

                        return null;
                    }),

                    Dispose = (EdgeFunc)(async p =>
                    {
                        EnsureNotDisposed();

                        await DisposeAll();

                        return null;
                    })
                };

                return proxy;
            }
            catch (Exception ex)
            {
                _logger.Error(ex.Message, ex);
                throw;
            }
        }
    }
}
