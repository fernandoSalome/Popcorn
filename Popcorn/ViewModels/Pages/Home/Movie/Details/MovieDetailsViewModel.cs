﻿using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Messaging;
using GalaSoft.MvvmLight.Threading;
using NLog;
using Popcorn.Helpers;
using Popcorn.Messaging;
using Popcorn.Models.Movie;
using Popcorn.Models.Subtitles;
using Popcorn.Services.Language;
using Popcorn.Services.Movies.Movie;
using Popcorn.Services.Movies.Trailer;
using Popcorn.Services.Subtitles;
using Popcorn.ViewModels.Pages.Home.Movie.Download;

namespace Popcorn.ViewModels.Pages.Home.Movie.Details
{
    /// <summary>
    /// Manage the movie
    /// </summary>
    public sealed class MovieDetailsViewModel : ViewModelBase
    {
        /// <summary>
        /// Logger of the class
        /// </summary>
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// The service used to interact with movies
        /// </summary>
        private readonly IMovieService _movieService;

        /// <summary>
        /// Token to cancel movie loading
        /// </summary>
        private CancellationTokenSource _cancellationLoadingToken;

        /// <summary>
        /// Token to cancel trailer loading
        /// </summary>
        private CancellationTokenSource _cancellationLoadingTrailerToken;

        /// <summary>
        /// Manage the movie download
        /// </summary>
        private DownloadMovieViewModel _downloadMovie;

        /// <summary>
        /// Specify if a movie is downloading
        /// </summary>
        private bool _isDownloadingMovie;

        /// <summary>
        /// Specify if a movie is loading
        /// </summary>
        private bool _isMovieLoading;

        /// <summary>
        /// Specify if a trailer is playing
        /// </summary>
        private bool _isPlayingTrailer;

        /// <summary>
        /// Specify if a trailer is loading
        /// </summary>
        private bool _isTrailerLoading;

        /// <summary>
        /// The movie to manage
        /// </summary>
        private MovieJson _movie = new MovieJson();

        /// <summary>
        /// The movie trailer service
        /// </summary>
        private readonly IMovieTrailerService _movieTrailerService;

        /// <summary>
        /// The service used to interact with subtitles
        /// </summary>
        private readonly ISubtitlesService _subtitlesService;

        /// <summary>
        /// True if subtitles are loading
        /// </summary>
        private bool _loadingSubtitles;

        /// <summary>
        /// Initializes a new instance of the MovieDetailsViewModel class.
        /// </summary>
        /// <param name="movieService">Service used to interact with movies</param>
        /// <param name="languageService">Language service</param>
        /// <param name="movieTrailerService">The movie trailer service</param>
        /// <param name="subtitlesService">The subtitles service</param>
        public MovieDetailsViewModel(IMovieService movieService, ILanguageService languageService, IMovieTrailerService movieTrailerService, ISubtitlesService subtitlesService)
        {
            _movieTrailerService = movieTrailerService;
            _movieService = movieService;
            _subtitlesService = subtitlesService;
            _cancellationLoadingToken = new CancellationTokenSource();
            _cancellationLoadingTrailerToken = new CancellationTokenSource();
            DownloadMovie = new DownloadMovieViewModel(subtitlesService, languageService);
            RegisterMessages();
            RegisterCommands();
        }

        /// <summary>
        /// The selected movie to manage
        /// </summary>
        public MovieJson Movie
        {
            get { return _movie; }
            set { Set(() => Movie, ref _movie, value); }
        }

        /// <summary>
        /// True if subtitles are loading
        /// </summary>
        public bool LoadingSubtitles
        {
            get { return _loadingSubtitles; }
            set { Set(() => LoadingSubtitles, ref _loadingSubtitles, value); }
        }

        /// <summary>
        /// Indicates if a movie is loading
        /// </summary>
        public bool IsMovieLoading
        {
            get { return _isMovieLoading; }
            set { Set(() => IsMovieLoading, ref _isMovieLoading, value); }
        }

        /// <summary>
        /// Manage the movie download
        /// </summary>
        public DownloadMovieViewModel DownloadMovie
        {
            get { return _downloadMovie; }
            set { Set(() => DownloadMovie, ref _downloadMovie, value); }
        }

        /// <summary>
        /// Specify if a trailer is loading
        /// </summary>
        public bool IsTrailerLoading
        {
            get { return _isTrailerLoading; }
            set { Set(() => IsTrailerLoading, ref _isTrailerLoading, value); }
        }

        /// <summary>
        /// Specify if a trailer is playing
        /// </summary>
        public bool IsPlayingTrailer
        {
            get { return _isPlayingTrailer; }
            set { Set(() => IsPlayingTrailer, ref _isPlayingTrailer, value); }
        }

        /// <summary>
        /// Specify if a movie is downloading
        /// </summary>
        public bool IsDownloadingMovie
        {
            get { return _isDownloadingMovie; }
            set { Set(() => IsDownloadingMovie, ref _isDownloadingMovie, value); }
        }

        /// <summary>
        /// Command used to load the movie
        /// </summary>
        public RelayCommand<MovieJson> LoadMovieCommand { get; private set; }

        /// <summary>
        /// Command used to stop loading the trailer
        /// </summary>
        public RelayCommand StopLoadingTrailerCommand { get; private set; }

        /// <summary>
        /// Command used to play the movie
        /// </summary>
        public RelayCommand PlayMovieCommand { get; private set; }

        /// <summary>
        /// Command used to play the trailer
        /// </summary>
        public RelayCommand PlayTrailerCommand { get; private set; }

        /// <summary>
        /// Cleanup resources
        /// </summary>
        public override void Cleanup()
        {
            StopLoadingMovie();
            StopPlayingMovie();
            StopLoadingTrailer();
            StopPlayingTrailer();
            base.Cleanup();
        }

        /// <summary>
        /// Load the movie's subtitles asynchronously
        /// </summary>
        /// <param name="movie">The movie</param>
        private void LoadSubtitles(MovieJson movie)
        {
            Logger.Debug(
                $"Load subtitles for movie: {movie.Title}");
            Movie = movie;
            LoadingSubtitles = true;
            Task.Run(() =>
            {
                try
                {
                    var languages = _subtitlesService.GetSubLanguages().ToList();

                    var imdbId = 0;
                    if (int.TryParse(new string(movie.ImdbCode
                        .SkipWhile(x => !char.IsDigit(x))
                        .TakeWhile(char.IsDigit)
                        .ToArray()), out imdbId))
                    {
                        var subtitles = _subtitlesService.SearchSubtitlesFromImdb(
                            languages.Select(lang => lang.SubLanguageID).Aggregate((a, b) => a + "," + b),
                            imdbId.ToString());
                        DispatcherHelper.CheckBeginInvokeOnUI(() =>
                        {
                            movie.AvailableSubtitles =
                                new ObservableCollection<Subtitle>(subtitles.OrderBy(a => a.LanguageName)
                                    .Select(sub => new Subtitle
                                    {
                                        Sub = sub
                                    }).GroupBy(x => x.Sub.LanguageName,
                                        (k, g) =>
                                            g.Aggregate(
                                                (a, x) =>
                                                    (Convert.ToDouble(x.Sub.Rating, CultureInfo.InvariantCulture) >=
                                                     Convert.ToDouble(a.Sub.Rating, CultureInfo.InvariantCulture))
                                                        ? x
                                                        : a)));
                            movie.AvailableSubtitles.Insert(0, new Subtitle
                            {
                                Sub = new OSDBnet.Subtitle
                                {
                                    LanguageName = LocalizationProviderHelper.GetLocalizedValue<string>("NoneLabel")
                                }
                            });

                            movie.SelectedSubtitle = movie.AvailableSubtitles.FirstOrDefault();
                            LoadingSubtitles = false;
                        });
                    }
                }
                catch (Exception)
                {
                    DispatcherHelper.CheckBeginInvokeOnUI(() =>
                    {
                        LoadingSubtitles = false;
                    });
                }
            });
        }

        /// <summary>
        /// Register messages
        /// </summary>
        private void RegisterMessages()
        {
            Messenger.Default.Register<StopPlayingTrailerMessage>(
                this,
                message => { StopPlayingTrailer(); });

            Messenger.Default.Register<StopPlayingMovieMessage>(
                this,
                message => { StopPlayingMovie(); });

            Messenger.Default.Register<ChangeLanguageMessage>(
                this,
                async message =>
                {
                    if (!string.IsNullOrEmpty(Movie?.ImdbCode))
                        await _movieService.TranslateMovieAsync(Movie);
                });
        }

        /// <summary>
        /// Register commands
        /// </summary>
        private void RegisterCommands()
        {
            LoadMovieCommand = new RelayCommand<MovieJson>(LoadMovie);

            PlayMovieCommand = new RelayCommand(() =>
            {
                IsDownloadingMovie = true;
                Messenger.Default.Send(new DownloadMovieMessage(Movie));
            });

            PlayTrailerCommand = new RelayCommand(async () =>
            {
                IsPlayingTrailer = true;
                IsTrailerLoading = true;
                await _movieTrailerService.LoadTrailerAsync(Movie, _cancellationLoadingTrailerToken.Token);
                IsTrailerLoading = false;
            });

            StopLoadingTrailerCommand = new RelayCommand(StopLoadingTrailer);
        }

        /// <summary>
        /// Load the requested movie
        /// </summary>
        /// <param name="movie">The movie to load</param>
        private void LoadMovie(MovieJson movie)
        {
            var watch = Stopwatch.StartNew();

            Messenger.Default.Send(new LoadMovieMessage());
            IsMovieLoading = true;

            Movie = movie;
            IsMovieLoading = false;
            Movie.FullHdAvailable = movie.Torrents.Any(torrent => torrent.Quality == "1080p");
            LoadSubtitles(Movie);

            watch.Stop();
            var elapsedMs = watch.ElapsedMilliseconds;
            Logger.Debug($"LoadMovie ({movie.ImdbCode}) in {elapsedMs} milliseconds.");
        }

        /// <summary>
        /// Stop loading the movie
        /// </summary>
        private void StopLoadingMovie()
        {
            Logger.Info(
                $"Stop loading movie: {Movie.Title}.");

            IsMovieLoading = false;
            _cancellationLoadingToken.Cancel(true);
            _cancellationLoadingToken = new CancellationTokenSource();
        }

        /// <summary>
        /// Stop playing the movie's trailer
        /// </summary>
        private void StopLoadingTrailer()
        {
            Logger.Info(
                $"Stop loading movie's trailer: {Movie.Title}.");

            IsTrailerLoading = false;
            _cancellationLoadingTrailerToken.Cancel(true);
            _cancellationLoadingTrailerToken = new CancellationTokenSource();
            StopPlayingTrailer();
        }

        /// <summary>
        /// Stop playing the movie's trailer
        /// </summary>
        private void StopPlayingTrailer()
        {
            Logger.Info(
                $"Stop playing movie's trailer: {Movie.Title}.");

            IsPlayingTrailer = false;
        }

        /// <summary>
        /// Stop playing a movie
        /// </summary>
        private void StopPlayingMovie()
        {
            Logger.Info(
                $"Stop playing movie: {Movie.Title}.");

            IsDownloadingMovie = false;
            DownloadMovie.StopDownloadingMovie();
        }
    }
}