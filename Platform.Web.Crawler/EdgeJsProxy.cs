using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using Platform.Data.Core.Pairs;
using Platform.Data.Core.Sequences;
using Platform.Helpers.Threading;

using EdgeFunc = System.Func<object, System.Threading.Tasks.Task<object>>;

#pragma warning disable 1998

// ReSharper disable ForCanBeConvertedToForeach
// ReSharper disable RedundantLambdaParameterType

namespace Platform.Web.Crawler
{
    public class EdgeJsProxy
    {
        private const string DefaultDataFile = "db.links";

        private readonly ConcurrentBag<Task> _tasks;
        private ILog _logger;
        private CancellationTokenSource _cancellationSource;

        private string _dataPath;

        private LinksMemoryManager _memoryManager;
        private Links _links;

        private ulong _pageMarker;
        private ulong _sequencesMarker;

        private bool _disposed;

        public EdgeJsProxy()
        {
            _tasks = new ConcurrentBag<Task>();

            _disposed = false;
        }

        private ulong CreatePoint()
        {
            return _links.Create(LinksConstants.Itself, LinksConstants.Itself);
        }

        private void AllocateMarker(ref ulong currentMarker, out ulong marker, string markerName)
        {
            marker = _links.Exists(currentMarker) ? currentMarker : CreatePoint();

            if (marker != currentMarker) throw new InvalidOperationException(string.Format("Не удалось создать {0} по ожидаемому адресу {1}", markerName, currentMarker));

            currentMarker++;
        }

        private void AllocateMarkers()
        {
            ulong currentMarker = UnicodeMap.MapSize + 1;

            AllocateMarker(ref currentMarker, out _pageMarker, "маркер страницы");
            AllocateMarker(ref currentMarker, out _sequencesMarker, "маркер последовательности");
        }

        private async Task WaitAll()
        {
            Task task;
            while (_tasks.TryTake(out task))
                await task;
        }

        private async Task<object> StopAll()
        {
            EnsureNotDisposed();

            _cancellationSource.Cancel();

            await WaitAll();

            _cancellationSource = new CancellationTokenSource();

            return true;
        }

        private async Task DisposeAll()
        {
            _cancellationSource.Cancel();

            await WaitAll();

            _links.Dispose();
            _memoryManager.Dispose();

            _disposed = true;
        }

        private void EnsureNotDisposed()
        {
            if (_disposed) throw new ObjectDisposedException("EdgeJsProxy");
        }

        public async Task<object> Invoke(dynamic input)
        {
            EnsureNotDisposed();

            try
            {
                var logPath = input.logPath as string;

                new LogService().Configure(logPath);
                _logger = LogManager.GetLogger("default");

                _cancellationSource = new CancellationTokenSource();

                _dataPath = input.dataPath as string;

                if (string.IsNullOrWhiteSpace(_dataPath))
                    _dataPath = DefaultDataFile;

                _memoryManager = new LinksMemoryManager(_dataPath, 64 * 1024 * 1024);
                _links = new Links(_memoryManager);

                new UnicodeMap(_links).Init();

                AllocateMarkers();

                var sequencesOptions = new SequencesOptions
                {
                    UseSequenceMarker = true,
                    SequenceMarkerLink = _sequencesMarker
                };

                var sequences = new Sequences(_links, sequencesOptions);

                var pagesRepository = new PagesService(_links, sequences, _pageMarker);

                var proxy = new
                {
                    StartSearch = (EdgeFunc)(async (dynamic p) =>
                    {
                        EnsureNotDisposed();

                        await WaitAll();

                        var query = (string) p.query;
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
                                    task = Task.Factory.StartNew(() => crawler.Start(uri));
                                    _tasks.Add(task);
                                }
                                else
                                {
                                    task = task.ContinueWith(t => crawler.Start(uri));
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
