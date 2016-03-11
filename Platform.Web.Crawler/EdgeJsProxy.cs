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

#pragma warning disable 1998

// ReSharper disable ForCanBeConvertedToForeach
// ReSharper disable RedundantLambdaParameterType

namespace Platform.Web.Crawler
{
    public class EdgeJsProxy
    {
        private const string DefaultDatabaseFilename = "db.links";

        private readonly ConcurrentBag<Task> _tasks;
        private readonly ILog _logger;
        private CancellationTokenSource _cancellationSource;

        private string _databasePath;

        private LinksMemoryManager _memoryManager;
        private Links _links;

        private ulong _pageMarker;
        private ulong _sequencesMarker;

        private bool _disposed;

        public EdgeJsProxy()
        {
            _tasks = new ConcurrentBag<Task>();

            new LogService().Configure();
            _logger = LogManager.GetLogger("default");

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

        public async Task<object> Invoke(object input)
        {
            if (_disposed) throw new ObjectDisposedException("EdgeJsProxy");

            try
            {
                _databasePath = input as string;

                if (string.IsNullOrWhiteSpace(_databasePath))
                    _databasePath = DefaultDatabaseFilename;

                _cancellationSource = new CancellationTokenSource();

                _memoryManager = new LinksMemoryManager(_databasePath, 64 * 1024 * 1024);
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
                    StartSearch = (Func<object, Task<object>>)(async (dynamic p) =>
                    {
                        var query = (string) p.query;
                        var pageFound = (Func<object, Task<object>>)p.pageFound;
                        var searchFinished = (Func<object, Task<object>>)p.searchFinished;
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

                    StopSearch = (Func<object, Task<object>>)(async p =>
                    {
                        _cancellationSource.Cancel();

                        Task task;
                        while (_tasks.TryTake(out task))
                            await task;

                        _cancellationSource = new CancellationTokenSource();

                        return null;
                    }),

                    StartCrawl = (Func<object, Task<object>>)(async (dynamic p) =>
                    {
                        var urls = ((object[])p.urls).Cast<string>().ToArray();
                        var pageCrawled = (Func<object, Task<object>>)p.pageCrawled;

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

                    StopCrawl = (Func<object, Task<object>>)(async p =>
                    {
                        _cancellationSource.Cancel();

                        Task task;
                        while (_tasks.TryTake(out task))
                            await task;

                        _cancellationSource = new CancellationTokenSource();

                        return null;
                    }),

                    Reset = (Func<object, Task<object>>)(async p =>
                    {
                        if (!_disposed)
                        {
                            _cancellationSource.Cancel();

                            Task task;
                            while (_tasks.TryTake(out task))
                                await task;

                            _links.Dispose();
                            _memoryManager.Dispose();

                            _disposed = true;
                        }

                        File.Delete(_databasePath);

                        return null;
                    }),

                    Dispose = (Func<object, Task<object>>)(async p =>
                    {
                        if (_disposed) throw new ObjectDisposedException("EdgeJsProxy");

                        _cancellationSource.Cancel();

                        Task task;
                        while (_tasks.TryTake(out task))
                            await task;

                        _links.Dispose();
                        _memoryManager.Dispose();

                        _disposed = true;

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
