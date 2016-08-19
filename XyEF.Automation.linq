<Query Kind="Program">
  <Reference>&lt;RuntimeDirectory&gt;\System.Net.Http.dll</Reference>
  <NuGetReference>Newtonsoft.Json</NuGetReference>
  <NuGetReference>reactiveui</NuGetReference>
  <NuGetReference>Splat</NuGetReference>
</Query>

}

#region namespace XyEF.Automation
namespace XyEF.Automation
{
    using Splat;
    using XyEF.Automation.Model;
    using XyEF.Automation.Services;
    using Xy.DmSharp;
    using Xy.Licensing;
    using Xy.Logging;

    public static class Program
    {
        //[STAThread]
        public static void Main()
        {
            Initialize();

            LicenseChecker.ThrowIfNotActivated();

            var engine = new GameEngine();

            CommandHandler.StartHandleInput().Wait();
        }

        private static void Initialize()
        {
            // ensure a fresh process/appdomain is used
            Util.NewProcess = true;

            // adds current directory into environment path
            var path = Environment.GetEnvironmentVariable("PATH");
            path = string.Concat(path, ";", Path.GetDirectoryName(Util.CurrentQueryPath));
            Environment.SetEnvironmentVariable("PATH", path);

            // initialize logger
            var logger = new StylizedConsoleLogger { Level = LogLevel.Debug };
            Locator.CurrentMutable.RegisterConstant(logger, typeof(ILogger));

            DmServices.Initialize();
        }
    }
}
#endregion

#region namespace XyEF.Automation.Model
namespace XyEF.Automation.Model
{
    using System.Drawing;
    using System.Reactive;
    using System.Reactive.Disposables;
    using System.Reactive.Linq;
    using System.Reactive.Subjects;
    using System.Threading.Tasks;
    using ReactiveUI;
    using Splat;
    using XyEF.Automation.Services;
    using Xy.Logging;
    using Xy.Reactive;

    public enum MenuTab { Quest, Unit, Dungeon, Artifact, Battle, Shop }

    public class GameEngine : ReactiveObject, IEnableLogger
    {
        #region public bool IsInMainScreen { get; }
        private ObservableAsPropertyHelper<bool> m_IsInMainScreen;
        /// <summary>Indicate if the game in at the main screen. With nothing else blocking the view, EXCEPT guild chat overlay.</summary>
        public bool IsInMainScreen => this.m_IsInMainScreen.Value;
        #endregion
        #region public MenuTab? SelectedMenuTab { get; }
        private ObservableAsPropertyHelper<MenuTab?> m_SelectedMenuTab;
        /// <summary>Indicate which of the main menu tab is selected.</summary>
        public MenuTab? SelectedMenuTab => this.m_SelectedMenuTab.Value;
        #endregion

        private readonly GameStats stats = new GameStats();
        private bool shouldOpenChest = true;
        private bool shouldWatchAd = true;
        private bool isWatchingAd = false;

        public GameEngine()
        {
            // initialize
            shouldOpenChest = true;

            // update IsInMainScreen
            var gems = CombineFilesPath(@"asset\icons\", "gem.*.bmp");
            Observable.Interval(TimeSpan.FromMilliseconds(250))
                .Select(_ => DmServices.Image.PictureExists(Coords.ResourceArea, gems))
                .ToProperty(this, x => x.IsInMainScreen, out m_IsInMainScreen);
            //            this.WhenAnyValue(x => x.IsInMainScreen)
            //                .Dump("IsInMainScreen");


            // handle treasure chest
            //            Observable.Interval(TimeSpan.FromMilliseconds(250))
            //                .Select(_ => DmServices.Image.GetPictureLocation(Coords.Treasure.ChestSpawnArea, @"asset\icons\treasure-chest.bmp", 0.6))
            //                .Where(x => !x.IsEmpty)
            //                .DistinctUntilChanged()
            //                .Select(x => x.ToString())
            //                .Dump("treasure chest");
            Observable.Interval(TimeSpan.FromMilliseconds(250))
                .Select(_ => DmServices.Image.GetPictureLocation(Coords.Treasure.ChestSpawnArea, @"asset\icons\treasure-chest.bmp", 0.6))
                .Where(x => !x.IsEmpty)
                .Where(_ => shouldOpenChest)
                .Do(x => DmServices.Input.LeftClick(x + Coords.Treasure.ChestOffset))
                .Throttle(TimeSpan.FromMilliseconds(250))
                .Subscribe(_ => this.Log().Info("一个宝箱被开启了!"));

            // watching ads
            var webAdCloseButtons = CombineFilesPath(@"asset\ads\", "web-ad-close-button.*.bmp");
            //            Observable.Interval(TimeSpan.FromMilliseconds(250))
            //                .Select(_ => DmServices.Image.GetPictureLocation(Coords.Ads.WatchAdConfirmationArea, @"asset\ads\watch-ad-confirmation.bmp"))
            //                .Where(x => !x.IsEmpty)
            //                .DistinctUntilChanged()
            //                .Select(x => x.ToString())
            //                .Dump("ad confirm");
            //            Observable.Interval(TimeSpan.FromMilliseconds(250))
            //                .Select(_ => DmServices.Image.GetPictureLocation(Coords.Ads.AdUnavailableMessageArea, @"asset\ads\ad-unavailable-message.bmp"))
            //                .Where(x => !x.IsEmpty)
            //                .DistinctUntilChanged()
            //                .Select(x => x.ToString())
            //                .Dump("ad unavailable");
            //            Observable.Interval(TimeSpan.FromMilliseconds(250))
            //                .Select(_ => DmServices.Image.GetPictureLocation(Coords.Ads.WebAdCloseButtonArea, webAdCloseButtons, 0.8))
            //                .Where(x => !x.IsEmpty)
            //                .DistinctUntilChanged()
            //                .Select(x => x.ToString())
            //                .Dump("close button");
            Observable.Interval(TimeSpan.FromMilliseconds(250))
                .Where(_ => shouldWatchAd && !isWatchingAd)
                .Where(_ => DmServices.Image.PictureExists(Coords.Ads.WatchAdConfirmationArea, @"asset\ads\watch-ad-confirmation.bmp"))
                .Subscribe(async _ =>
                {
                    // retry guard
                    isWatchingAd = true;
                    using (Disposable.Create(() => isWatchingAd = false))
                    {
                        DmServices.Image.PrintScreen(Coords.PhoneScreen, $"screenshot\\confirm-watch-ad.bmp");
                        
                        // watch ad
                        DmServices.Input.LeftClick(Coords.Ads.WatchAdButton);
                        await Task.Delay(500);

                        DmServices.Image.PrintScreen(Coords.PhoneScreen, $"screenshot\\no-ad-message.bmp");
                        
                        // confirm there is no ad available
                        int retry = 0;
                        while (!DmServices.Image.PictureExists(Coords.Ads.AdUnavailableMessageArea, @"asset\ads\ad-unavailable-message.bmp"))
                        {
                            if (retry++ > 5)
                            {
                                this.Log().Error("广告处理失败: 预期的[今天也开心~转世!!]消息框未出现");
                                return;
                            }

                            await Task.Delay(500);
                        }
                        DmServices.Input.LeftClick(Coords.Ads.AdUnavailableConfirmButton);
                        await Task.Delay(1500);

                        if (IsInMainScreen)
                        {
                            this.Log().Info("即使没有广告 也依旧有礼物!");
                            return;
                        }

                        retry = 0;
                        while (!IsInMainScreen)
                        {
                            if (retry++ > 5)
                            {
                                this.Log().Error("弹出广告处理失败: 预期的[x]关闭按钮未出现");
                                this.Log().Error("弹出广告处理失败: 如果游戏卡在网页广告的界面");
                                this.Log().Error("弹出广告处理失败: 请用`ss`命令截图 并传给我");
                                return;
                            }

                            var location = DmServices.Image.GetPictureLocation(Coords.Ads.WebAdCloseButtonArea, webAdCloseButtons, 0.8);
                            if (!location.IsEmpty)
                            {
                                DmServices.Input.LeftClick(location + Coords.Ads.WebAdCloseButtonOffset);
                                await Task.Delay(1000);

                                if (IsInMainScreen)
                                {
                                    this.Log().Info("成功的解决掉宝箱守护者 并获取了奖励!");
                                    return;
                                }
                            }

                            await Task.Delay(500);
                        }
                    }
                });

            AutoPurchaseUnit();
        }

        private void AutoPurchaseUnit()
        {
            var locked = false;

            //            Observable.Interval(TimeSpan.FromMilliseconds(1000))
            //                .Select(_ => DmServices.Image.GetPictureLocation(Coords.Unit.RefreshButtonArea, @"asset\unit\refresh-button.bmp"))
            //                .Where(x => !x.IsEmpty)
            //                .Select(x => x.ToString())
            //                .Dump(@"unit\refresh-unit");
            Observable.Interval(TimeSpan.FromMilliseconds(60000)).StartWith(default(long))
                .Select(_ => DmServices.Image.GetPictureLocation(Coords.Unit.RefreshButtonArea, @"asset\unit\refresh-button.bmp"))
                .Where(x => !locked && !x.IsEmpty)
                .Subscribe(async x =>
                {
                    locked = true;
                    using (Disposable.Create(() => locked = false))
                    {
                        await Task.Delay(2000);

                        // TODO: add fix for too early refresh

                        DmServices.Input.LeftClick(x + Coords.Unit.RefreshButtonOffset);
                        this.Log().Info("士兵列表以刷新");
                        await Task.Delay(1500);

                        DmServices.Image.PrintScreen(Coords.PhoneScreen, $@"screenshot\unit\{DateTime.Now:yyyy-MM-dd HH-mm-ss.fff} refreshed-unit.bmp");
                    }
                });

            var unitPurchaseButtons = CombineFilesPath(@"asset\unit\", "*-medal-purchase.bmp");
            //            Observable.Interval(TimeSpan.FromMilliseconds(1000))
            //                .Select(_ => DmServices.Image.GetPictureLocation(Coords.PhoneScreen, unitPurchaseButtons, 0.9))
            //                .Where(x => !x.IsEmpty)
            //                .Select(x => x.ToString())
            //                .Dump(@"unit\unit-purchase-button");
            Observable.Interval(TimeSpan.FromMilliseconds(1000))
                .Select(_ => DmServices.Image.GetPictureLocation(Coords.Unit.UnitPurchaseButtonArea, unitPurchaseButtons, 0.9))
                .Where(x => !locked && !x.IsEmpty)
                .Subscribe(async x =>
                {
                    locked = true;
                    using (Disposable.Create(() => locked = false))
                    {
                        DmServices.Input.LeftClick(x + Coords.Unit.UnitPurchaseButtonOffset);
                        await Task.Delay(1500);

                        if (DmServices.Image.PictureExists(Coords.Unit.ConfirmPurchaseCostArea, @"asset\unit\cost-medal.bmp"))
                        {
                            this.Log().Info("准备购买士兵");
                            DmServices.Image.PrintScreen(Coords.Unit.ConfirmPurchaseScreenshotArea, $@"screenshot\unit\{DateTime.Now:yyyy-MM-dd HH-mm-ss.fff} unit-purchase.bmp");
                            DmServices.Input.LeftClick(Coords.Unit.ConfirmPurchaseButton);
                        }
                    }
                });
        }

        /// <summary>Join all files matched by the filter, according to DM's path syntax</summary>
        private static string CombineFilesPath(string directory, string filter)
        {
            return string.Join("|",
                Directory.GetFiles(Path.Combine(DmServices.ResourcePath, directory), filter)
                .Select(x => DmServices.ResourceUri.MakeRelativeUri(new Uri(x)).OriginalString)
                // aesthetic fix
                .Select(x => x.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)));
        }
    }
    public class GameStats
    {
        public readonly DateTime StartTime = DateTime.Now;
        public TimeSpan Uptime => DateTime.Now - StartTime;

        private int openedChest = 0;
        public int OpenedChest => openedChest;
        public int IncrementOpenedChest() => Interlocked.Increment(ref openedChest);

        private int watchedAd = 0;
        public int WatchedAd => watchedAd;
        public int IncrementWatchedAd() => Interlocked.Increment(ref watchedAd);
    }

    public static partial class CommandHandler
    {
        private static readonly CommandCollection commands;

        static CommandHandler()
        {
            Action notImplemented = () => { throw new NotImplementedException(); };

            commands = new CommandCollection()
            {
                new Command("ss")
                {
                    new CommandArgs(() => DmServices.Image.PrintScreen(Coords.PhoneScreen)),
                    new CommandArgs("--full", () => DmServices.Image.PrintScreen(Coords.EmulatorArea)),
                    CommandArgs.Create("{x1} {y1} {x2} {y2}",
                        new Regex(string.Join(" ", Enumerable.Repeat(@"(?<args>\d+)", 4))),
                        m => DmServices.Image.PrintScreen(new RECT
                        (
                            int.Parse(m.Groups["args"].Captures[0].Value),
                            int.Parse(m.Groups["args"].Captures[1].Value),
                            int.Parse(m.Groups["args"].Captures[2].Value),
                            int.Parse(m.Groups["args"].Captures[3].Value)
                        ))),
                    new CommandArgs("--coords {name}", notImplemented),
                },
                new Command("findpic")
                {
                    new CommandArgs("--coord {name} {pic}", notImplemented),
                }
            };
        }

        public static async Task StartHandleInput()
        {
            while (true)
            {
                try
                {
                    await HandleInput();
                }
                catch (Exception ex)
                {
                    Logger.Instance.ErrorException("Unhandled error", ex);
                }
            }
        }
        private static async Task HandleInput()
        {
            var input = await Util.ReadLineAsync("请输入命令", null, commands.GetSuggestionList());
            Logger.Instance.Info("> " + input);

            commands.ProcessInput(input);
            await Task.CompletedTask;
        }

        private static readonly TypeNamedLogger Logger = new TypeNamedLogger();
    }
    public static partial class CommandHandler
    {
        public class CommandCollection : List<Command>
        {
            public IEnumerable<string> GetSuggestionList()
            {
                return this.SelectMany(x => x.GetSuggestions());
            }

            public void ProcessInput(string input)
            {
                if (string.IsNullOrEmpty(input)) return;

                var parts = input.Trim().Split(new[] { ' ' }, 2);
                var command = parts[0];
                var args = parts.Length == 2 ? parts[1] : null;

                var matchedCommand = this.FirstOrDefault(x => x.Name == parts[0]);
                if (matchedCommand == null)
                {
                    Console.WriteLine("'{0}'命令并不存在", parts[0]);
                    return;
                }

                var matchedArgs = matchedCommand.FirstOrDefault(x => x.CanMatch(parts.Length == 2 ? parts[1] : null));
                if (matchedArgs == null)
                {
                    Console.WriteLine("无效的参数. 参考以下使用方式:\n" + string.Join("\n", matchedCommand.GetSuggestions()));
                }

                matchedArgs.Invoke(args);
            }
        }
        public class Command : List<CommandArgs>
        {
            public string Name { get; }

            public Command(string name)
            {
                this.Name = name;
            }

            public IEnumerable<string> GetSuggestions()
            {
                foreach (var args in this)
                    yield return args.Template != null
                        ? Name + " " + args.Template
                        : Name;
            }
        }
        public class CommandArgs
        {
            public string Template { get; }
            public Func<string, bool> CanMatch { get; }
            public Action<string> Invoke { get; }

            public CommandArgs(Action callback)
            {
                this.CanMatch = x => x == null;
                this.Invoke = _ => callback();
            }
            public CommandArgs(string template, Action callback)
            {
                this.Template = template;
                this.CanMatch = x => x == template;
                this.Invoke = _ => callback();
            }
            public CommandArgs(string template, Func<string, bool> canMatch, Action<string> callback)
            {
                this.Template = template;
                this.CanMatch = canMatch;
                this.Invoke = callback;
            }

            public static CommandArgs Empty(Action callback) => new CommandArgs(callback);
            public static CommandArgs Create(string template, Action callback) => new CommandArgs(template, callback);
            public static CommandArgs Create(string template, Regex argsMatcher, Action<Match> callback) =>
                new CommandArgs(template, argsMatcher.IsMatch, x => callback(argsMatcher.Match(x)));
        }
    }

    public static class Coords
    {
        public static readonly RECT EmulatorArea = new RECT(0, 0, 2000, 2000);
        /// <summary>Again this isnt a perfect match. There is some error margin.</summary>
        public static readonly RECT PhoneScreen = new RECT(296, 30, 830, 1000).Extend(10, 5);
        public static readonly RECT ResourceArea = new RECT(296, 446, 830, 446 + 21 + 1).Extend(15, 10);
        
        public static readonly Point BackButton = new Point(118, 22);

        public static class Treasure
        {
            public static readonly RECT ChestSpawnArea = new RECT(296, 310, 830, 310 + 34 + 1).Extend(15, 20);
            public static readonly Size ChestOffset = new Size(39 / 2, 34 / 2);
        }
        public static class Ads
        {
            public static readonly RECT WatchAdConfirmationArea = new RECT(454, 520, new Size(214, 22)).Extend(15, 10);
            public static readonly Point WatchAdButton = new Point(478, 603);

            public static readonly RECT AdUnavailableMessageArea = new RECT(400, 437, new Size(220, 117)).Extend(15, 10);
            public static readonly Point AdUnavailableConfirmButton = new Point(566, 703);

            public static readonly RECT WebAdCloseButtonArea = new RECT(296, 50, 830, 80).Extend(15, 10);
            public static readonly Size WebAdCloseButtonOffset = new Size(8, 8);
        }
        public static class Unit
        {
            public static readonly RECT RefreshButtonArea = new RECT(650, 473, new Size(118, 57)).Extend(15, 10);
            public static readonly Size RefreshButtonOffset = new Size(118 / 2, 57 / 2);

            public static readonly RECT UnitPurchaseButtonArea = new RECT(681, 538, 830, 905).Extend(15, 10);
            public static readonly Size UnitPurchaseButtonOffset = new Size(138 / 2, 71 / 2);

            public static readonly RECT ConfirmPurchaseCostArea = new RECT(485, 604, 620, 638).Extend(15, 10);
            public static readonly RECT ConfirmPurchaseScreenshotArea = new RECT(296, 470, 830, 756).Extend(15, 10);
            public static readonly Point ConfirmPurchaseButton = new Point(564, 698);
        }
    }

    public static class TwoDimentionalExtensions
    {
        public static Point OffsetBy(this Point p1, Point p2)
        {
            return new Point(p1.X + p2.X, p1.Y + p2.Y);
        }
        public static Point OffsetBy(this Point p1, int x, int y)
        {
            return new Point(p1.X + x, p1.Y + y);
        }

        /// <summary>Add some error magin to the area</summary>
        public static RECT Extend(this RECT area, int marginX, int marginY)
        {
            return new RECT(
                area.Left - marginX,
                area.Top - marginY,
                area.Right + marginX,
                area.Bottom + marginY);
        }
    }
}
#endregion
#region namespace XyEF.Automation.Services
namespace XyEF.Automation.Services
{
    using System.Drawing;
    using System.Reactive.Disposables;
    using System.Runtime.InteropServices;
    using Xy.DmSharp;
    using Splat;
    using API = Xy.DmSharp.DmSoft.API;
    using Xy.Logging;

    #region structures
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;

        public RECT(int left, int top, int right, int bottom)
        {
            this.Left = left;
            this.Top = top;
            this.Right = right;
            this.Bottom = bottom;
        }

        /// <remarks>1 pixel is added to both right and bottom, in order to be able to find the image</remarks>        
        public RECT(int left, int top, Size size)
        {
            this.Left = left;
            this.Top = top;
            this.Right = left + size.Width + 1;
            this.Bottom = top + size.Height + 1;
        }

        public override string ToString() => $"{{ X1={Left}, Y1={Top}, X2={Right}, Y2={Bottom} }}";
    }
    #endregion

    public static class DmServices
    {
        public static readonly string ResourcePath = Path.Combine(Path.GetDirectoryName(Util.CurrentQueryPath), "res");
        public static readonly Uri ResourceUri = new Uri(ResourcePath + @"\");
        public const string BlueStackAppPlayer = "BlueStacks App Player";
        public const string Background = nameof(Background), Foreground = nameof(Foreground);

        public static InputService Input { get; private set; }
        public static ImageService Image { get; private set; }

        public static void Initialize()
        {
            Logger.Instance.Debug("Initializing DmServices");

            var backgroundDM = new DmSoft();
            var foregroundDM = new DmSoft();
            var handle = API.FindWindow(backgroundDM, "", BlueStackAppPlayer);

            API.SetPath(backgroundDM, ResourcePath);
            API.SetPath(foregroundDM, ResourcePath);

            API.BindWindow(backgroundDM, handle, "dx2", "windows3", "windows", 1);
            API.BindWindow(foregroundDM, handle, "dx2", "normal", "windows", 1);
            API.SetWindowSize(foregroundDM, handle, 1044, 1000);

            Locator.CurrentMutable.RegisterConstant(backgroundDM, typeof(DmSoft), Background);
            Locator.CurrentMutable.RegisterConstant(foregroundDM, typeof(DmSoft), Foreground);

            Input = new InputService();
            Image = new ImageService();
        }

        private static readonly TypeNamedLogger Logger = new TypeNamedLogger();
    }

    public class InputService
    {
        private readonly DmSoft dm = Locator.Current.GetService<DmSoft>(DmServices.Background);

        public bool MoveTo(Point location) => API.MoveTo(dm, location.X, location.Y) != 0;
        public bool LeftClick() => API.LeftClick(dm) != 0;
        public bool LeftClick(Point location) => MoveTo(location) && LeftClick();

        private static readonly TypeNamedLogger Logger = new TypeNamedLogger();
    }
    public class ImageService
    {
        private readonly DmSoft dm = Locator.Current.GetService<DmSoft>(DmServices.Background);

        public Point GetPictureLocation(RECT area, string picture, double similarity = 1)
        {
            Point result;
            return FindPicture(area, picture, out result, similarity: similarity) > -1
                ? result : Point.Empty;
        }
        public bool PictureExists(RECT area, string picture, double similarity = 1)
        {
            Point ignored;
            return FindPicture(area, picture, out ignored, similarity: similarity) > -1;
        }

        /// <summary>Find the first occurance of the image in the given area. Wrapping FindPic</summary>
        /// <param name="area>Region to scan for the image. Note: This area must be 1pixel larger than each axis in location+size.</param>
        /// <param name="picture">Relative or absolute path. Support multiple picture separated by |.</param>
        /// <param name="delta_color">Variance allowed per color channel(RGB) in hex. FFFFFF = any color goes.</param>
        /// <param name="similarity">1 for pixel-perfect.</param>
        /// <returns>Index of the picture found (zero-based). -1 If not found.</returns>
        public int FindPicture(RECT area, string picture, out Point location, string delta_color = "000000", double similarity = 1, ImageSearchDirection direction = default(ImageSearchDirection))
        {
            using (PerfWatch.WarnIfSlowerThan(100, x =>
                Logger.Instance.Warn("low performance warning : {0} FindPicture({1})",
                    x, new { area, picture, delta_color, similarity, direction })))
            {
                object x, y;
                var result = API.FindPic(dm, area.Left, area.Top, area.Right, area.Bottom, picture, delta_color, similarity, (int)direction, out x, out y);

                location = result > -1
                    ? new Point((int)x, (int)y)
                    : Point.Empty;

                return result;
            }
        }

        public bool PrintScreen(RECT area, string path = null)
        {
            path = path ?? $"screenshot\\{DateTime.Now:yyyy-MM-dd HH-mm-ss.fff}.bmp";

            if (API.Capture(dm, area.Left, area.Top, area.Right, area.Bottom, path) == 0)
            {
                Logger.Instance.Error("截图失败: args: {0}", new { area, path });
                return false;
            }

            // HyperLinq cannot be executed when UI thread is blocked
            //            var fullPath = Path.IsPathRooted(path) ?
            //                path : Path.Combine(DmServices.ResourcePath, path);
            //            
            //            Util.HorizontalRun(false,
            //                "截图以保存到: ",
            //                new Hyperlinq(() => Process.Start(Path.GetDirectoryName(fullPath)), Path.GetDirectoryName(path)),
            //                "\\",
            //                new Hyperlinq(() => Process.Start(fullPath), Path.GetFileName(path))
            //                )
            //                .Dump();

            Logger.Instance.Info("截图以保存到: " + path);
            return true;
        }

        //        [Obsolete("API.GetPicSize not working")]
        //        public Size GetPictureSize(string path)
        //        {
        //            var result = API.GetPicSize(dm, path);
        //            if (Regex.IsMatch(result, @"\d+,\d+"))
        //            {
        //                var size = result.Split(',');
        //                return new Size(int.Parse(size[0]), int.Parse(size[1]));
        //            }
        //            
        //            return Size.Empty;
        //        }

        private static readonly TypeNamedLogger Logger = new TypeNamedLogger();
    }

    public static class PerfWatch
    {
        public static IDisposable Time(Action<TimeSpan> log)
        {
            var sw = Stopwatch.StartNew();
            return Disposable.Create(() =>
            {
                sw.Stop();
                log(sw.Elapsed);
            });
        }

        public static IDisposable WarnIfSlowerThan(int milliseconds, Action<TimeSpan> log) => WarnIfSlowerThan(TimeSpan.FromMilliseconds(milliseconds), log);
        public static IDisposable WarnIfSlowerThan(TimeSpan duration, Action<TimeSpan> log)
        {
            var sw = Stopwatch.StartNew();
            return Disposable.Create(() =>
            {
                sw.Stop();
                if (sw.Elapsed > duration)
                {
                    log(sw.Elapsed);
                }
            });
        }
    }

    public enum ImageSearchDirection : int
    {
        /// <summary>From left to right, from top to bottom</summary>
        LRTB = 0,
        /// <summary>From left to right, from bottom to top</summary>
        LRBT = 1,
        /// <summary>From right to left, from top to bottom</summary>
        RLTB = 2,
        /// <summary>From right to left, from bottom to top</summary>
        RLBT = 3,
    }
}
#endregion

#region namespace Xy.DmSharp
namespace Xy.DmSharp
{
    using System.ComponentModel;
    using System.Drawing;
    using System.Runtime.InteropServices;

    public sealed partial class DmSoft : IDisposable
    {
        private readonly IntPtr dm = IntPtr.Zero;

        public DmSoft()
        {
            dm = API.CreateDM();
            if (dm == IntPtr.Zero)
                throw new InvalidComObjectException("Failed to create dm instance");
        }

        public static implicit operator IntPtr(DmSoft instance) => instance.dm;

        #region IDisposable Members

        public void Dispose()
        {
            DisposeManagedResources();
            DisposeNativeResources();
            GC.SuppressFinalize(this);
        }

        private void DisposeManagedResources()
        {

        }
        private void DisposeNativeResources()
        {
            if (dm != IntPtr.Zero)
            {
                API.UnBindWindow(dm);
                API.FreeDM();
            }
        }

        ~DmSoft()
        {
            DisposeNativeResources();
        }

        #endregion
    }
    public partial class DmSoft
    {
        internal static class API
        {
            private const string PluginName = @"lib\dm.dll";
            private const string WrapperLibraryName = @"lib\dmc.dll";

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern IntPtr CreateDM(string dmpath = PluginName);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FreeDM();

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string Ver(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetPath(IntPtr dm, string path);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string Ocr(IntPtr dm, int x1, int y1, int x2, int y2, string color, double sim);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FindStr(IntPtr dm, int x1, int y1, int x2, int y2, string str, string color, double sim, out object x, out object y);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetResultCount(IntPtr dm, string str);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetResultPos(IntPtr dm, string str, int index, out object x, out object y);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int StrStr(IntPtr dm, string s, string str);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SendCommand(IntPtr dm, string cmd);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int UseDict(IntPtr dm, int index);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string GetBasePath(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetDictPwd(IntPtr dm, string pwd);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string OcrInFile(IntPtr dm, int x1, int y1, int x2, int y2, string pic_name, string color, double sim);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int Capture(IntPtr dm, int x1, int y1, int x2, int y2, string file_);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int KeyPress(IntPtr dm, int vk);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int KeyDown(IntPtr dm, int vk);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int KeyUp(IntPtr dm, int vk);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int LeftClick(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int RightClick(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int MiddleClick(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int LeftDoubleClick(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int LeftDown(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int LeftUp(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int RightDown(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int RightUp(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int MoveTo(IntPtr dm, int x, int y);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int MoveR(IntPtr dm, int rx, int ry);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string GetColor(IntPtr dm, int x, int y);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string GetColorBGR(IntPtr dm, int x, int y);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string RGB2BGR(IntPtr dm, string rgb_color);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string BGR2RGB(IntPtr dm, string bgr_color);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int UnBindWindow(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int CmpColor(IntPtr dm, int x, int y, string color, double sim);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int ClientToScreen(IntPtr dm, int hwnd, ref object x, ref object y);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int ScreenToClient(IntPtr dm, int hwnd, ref object x, ref object y);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int ShowScrMsg(IntPtr dm, int x1, int y1, int x2, int y2, string msg, string color);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetMinRowGap(IntPtr dm, int row_gap);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetMinColGap(IntPtr dm, int col_gap);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FindColor(IntPtr dm, int x1, int y1, int x2, int y2, string color, double sim, int dir, out object x, out object y);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindColorEx(IntPtr dm, int x1, int y1, int x2, int y2, string color, double sim, int dir);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetWordLineHeight(IntPtr dm, int line_height);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetWordGap(IntPtr dm, int word_gap);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetRowGapNoDict(IntPtr dm, int row_gap);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetColGapNoDict(IntPtr dm, int col_gap);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetWordLineHeightNoDict(IntPtr dm, int line_height);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetWordGapNoDict(IntPtr dm, int word_gap);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetWordResultCount(IntPtr dm, string str);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetWordResultPos(IntPtr dm, string str, int index, out object x, out object y);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string GetWordResultStr(IntPtr dm, string str, int index);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string GetWords(IntPtr dm, int x1, int y1, int x2, int y2, string color, double sim);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string GetWordsNoDict(IntPtr dm, int x1, int y1, int x2, int y2, string color);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetShowErrorMsg(IntPtr dm, int show);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetClientSize(IntPtr dm, int hwnd, out object width, out object height);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int MoveWindow(IntPtr dm, int hwnd, int x, int y);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string GetColorHSV(IntPtr dm, int x, int y);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string GetAveRGB(IntPtr dm, int x1, int y1, int x2, int y2);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string GetAveHSV(IntPtr dm, int x1, int y1, int x2, int y2);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetForegroundWindow(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetForegroundFocus(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetMousePointWindow(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetPointWindow(IntPtr dm, int x, int y);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string EnumWindow(IntPtr dm, int parent, string title, string class_name, int filter);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetWindowState(IntPtr dm, int hwnd, int flag);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetWindow(IntPtr dm, int hwnd, int flag);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetSpecialWindow(IntPtr dm, int flag);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetWindowText(IntPtr dm, int hwnd, string text);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetWindowSize(IntPtr dm, int hwnd, int width, int height);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetWindowRect(IntPtr dm, int hwnd, out object x1, out object y1, out object x2, out object y2);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string GetWindowTitle(IntPtr dm, int hwnd);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string GetWindowClass(IntPtr dm, int hwnd);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetWindowState(IntPtr dm, int hwnd, int flag);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int CreateFoobarRect(IntPtr dm, int hwnd, int x, int y, int w, int h);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int CreateFoobarRoundRect(IntPtr dm, int hwnd, int x, int y, int w, int h, int rw, int rh);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int CreateFoobarEllipse(IntPtr dm, int hwnd, int x, int y, int w, int h);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int CreateFoobarCustom(IntPtr dm, int hwnd, int x, int y, string pic, string trans_color, double sim);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FoobarFillRect(IntPtr dm, int hwnd, int x1, int y1, int x2, int y2, string color);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FoobarDrawText(IntPtr dm, int hwnd, int x, int y, int w, int h, string text, string color, int align);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FoobarDrawPic(IntPtr dm, int hwnd, int x, int y, string pic, string trans_color);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FoobarUpdate(IntPtr dm, int hwnd);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FoobarLock(IntPtr dm, int hwnd);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FoobarUnlock(IntPtr dm, int hwnd);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FoobarSetFont(IntPtr dm, int hwnd, string font_name, int size, int flag);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FoobarTextRect(IntPtr dm, int hwnd, int x, int y, int w, int h);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FoobarPrintText(IntPtr dm, int hwnd, string text, string color);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FoobarClearText(IntPtr dm, int hwnd);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FoobarTextLineGap(IntPtr dm, int hwnd, int gap);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int Play(IntPtr dm, string file_);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FaqCapture(IntPtr dm, int x1, int y1, int x2, int y2, int quality, int delay, int time);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FaqRelease(IntPtr dm, int handle);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FaqSend(IntPtr dm, string server, int handle, int request_type, int time_out);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int Beep(IntPtr dm, int fre, int delay);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FoobarClose(IntPtr dm, int hwnd);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int MoveDD(IntPtr dm, int dx, int dy);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FaqGetSize(IntPtr dm, int handle);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int LoadPic(IntPtr dm, string pic_name);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FreePic(IntPtr dm, string pic_name);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetScreenData(IntPtr dm, int x1, int y1, int x2, int y2);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FreeScreenData(IntPtr dm, int handle);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int WheelUp(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int WheelDown(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetMouseDelay(IntPtr dm, string type_, int delay);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetKeypadDelay(IntPtr dm, string type_, int delay);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string GetEnv(IntPtr dm, int index, string name);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetEnv(IntPtr dm, int index, string name, string value);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SendString(IntPtr dm, int hwnd, string str);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int DelEnv(IntPtr dm, int index, string name);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string GetPath(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetDict(IntPtr dm, int index, string dict_name);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FindPic(IntPtr dm, int x1, int y1, int x2, int y2, string pic_name, string delta_color, double sim, int dir, out object x, out object y);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindPicEx(IntPtr dm, int x1, int y1, int x2, int y2, string pic_name, string delta_color, double sim, int dir);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetClientSize(IntPtr dm, int hwnd, int width, int height);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int ReadInt(IntPtr dm, int hwnd, string addr, int type_);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int ReadFloat(IntPtr dm, int hwnd, string addr);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int ReadDouble(IntPtr dm, int hwnd, string addr);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindInt(IntPtr dm, int hwnd, string addr_range, int int_value_min, int int_value_max, int type_);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindFloat(IntPtr dm, int hwnd, string addr_range, Single float_value_min, Single float_value_max);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindDouble(IntPtr dm, int hwnd, string addr_range, double double_value_min, double double_value_max);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindString(IntPtr dm, int hwnd, string addr_range, string string_value, int type_);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetModuleBaseAddr(IntPtr dm, int hwnd, string module_name);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string MoveToEx(IntPtr dm, int x, int y, int w, int h);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string MatchPicName(IntPtr dm, string pic_name);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int AddDict(IntPtr dm, int index, string dict_info);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int EnterCri(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int LeaveCri(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int WriteInt(IntPtr dm, int hwnd, string addr, int type_, int v);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int WriteFloat(IntPtr dm, int hwnd, string addr, Single v);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int WriteDouble(IntPtr dm, int hwnd, string addr, double v);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int WriteString(IntPtr dm, int hwnd, string addr, int type_, string v);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int AsmAdd(IntPtr dm, string asm_ins);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int AsmClear(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int AsmCall(IntPtr dm, int hwnd, int mode);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FindMultiColor(IntPtr dm, int x1, int y1, int x2, int y2, string first_color, string offset_color, double sim, int dir, out object x, out object y);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindMultiColorEx(IntPtr dm, int x1, int y1, int x2, int y2, string first_color, string offset_color, double sim, int dir);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string AsmCode(IntPtr dm, int base_addr);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string Assemble(IntPtr dm, string asm_code, int base_addr, int is_upper);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetWindowTransparent(IntPtr dm, int hwnd, int v);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string ReadData(IntPtr dm, int hwnd, string addr, int len);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int WriteData(IntPtr dm, int hwnd, string addr, string data);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindData(IntPtr dm, int hwnd, string addr_range, string data);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetPicPwd(IntPtr dm, string pwd);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int Log(IntPtr dm, string info);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindStrE(IntPtr dm, int x1, int y1, int x2, int y2, string str, string color, double sim);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindColorE(IntPtr dm, int x1, int y1, int x2, int y2, string color, double sim, int dir);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindPicE(IntPtr dm, int x1, int y1, int x2, int y2, string pic_name, string delta_color, double sim, int dir);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindMultiColorE(IntPtr dm, int x1, int y1, int x2, int y2, string first_color, string offset_color, double sim, int dir);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetExactOcr(IntPtr dm, int exact_ocr);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string ReadString(IntPtr dm, int hwnd, string addr, int type_, int len);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FoobarTextPrintDir(IntPtr dm, int hwnd, int dir);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string OcrEx(IntPtr dm, int x1, int y1, int x2, int y2, string color, double sim);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetDisplayInput(IntPtr dm, string mode);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetTime(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetScreenWidth(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetScreenHeight(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int BindWindowEx(IntPtr dm, int hwnd, string display, string mouse, string keypad, string public_desc, int mode);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string GetDiskSerial(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string Md5(IntPtr dm, string str);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string GetMac(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int ActiveInputMethod(IntPtr dm, int hwnd, string id);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int CheckInputMethod(IntPtr dm, int hwnd, string id);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FindInputMethod(IntPtr dm, string id);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetCursorPos(IntPtr dm, out object x, out object y);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int BindWindow(IntPtr dm, int hwnd, string display, string mouse, string keypad, int mode);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FindWindow(IntPtr dm, string class_name, string title_name);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetScreenDepth(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetScreen(IntPtr dm, int width, int height, int depth);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int ExitOs(IntPtr dm, int type_);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string GetDir(IntPtr dm, int type_);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetOsType(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FindWindowEx(IntPtr dm, int parent, string class_name, string title_name);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetExportDict(IntPtr dm, int index, string dict_name);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string GetCursorShape(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int DownCpu(IntPtr dm, int rate);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string GetCursorSpot(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SendString2(IntPtr dm, int hwnd, string str);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FaqPost(IntPtr dm, string server, int handle, int request_type, int time_out);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FaqFetch(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FetchWord(IntPtr dm, int x1, int y1, int x2, int y2, string color, string word);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int CaptureJpg(IntPtr dm, int x1, int y1, int x2, int y2, string file_, int quality);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FindStrWithFont(IntPtr dm, int x1, int y1, int x2, int y2, string str, string color, double sim, string font_name, int font_size, int flag, out object x, out object y);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindStrWithFontE(IntPtr dm, int x1, int y1, int x2, int y2, string str, string color, double sim, string font_name, int font_size, int flag);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindStrWithFontEx(IntPtr dm, int x1, int y1, int x2, int y2, string str, string color, double sim, string font_name, int font_size, int flag);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string GetDictInfo(IntPtr dm, string str, string font_name, int font_size, int flag);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SaveDict(IntPtr dm, int index, string file_);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetWindowProcessId(IntPtr dm, int hwnd);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string GetWindowProcessPath(IntPtr dm, int hwnd);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int LockInput(IntPtr dm, int lock1);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string GetPicSize(IntPtr dm, string pic_name);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetID(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int CapturePng(IntPtr dm, int x1, int y1, int x2, int y2, string file_);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int CaptureGif(IntPtr dm, int x1, int y1, int x2, int y2, string file_, int delay, int time);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int ImageToBmp(IntPtr dm, string pic_name, string bmp_name);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FindStrFast(IntPtr dm, int x1, int y1, int x2, int y2, string str, string color, double sim, out object x, out object y);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindStrFastEx(IntPtr dm, int x1, int y1, int x2, int y2, string str, string color, double sim);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindStrFastE(IntPtr dm, int x1, int y1, int x2, int y2, string str, string color, double sim);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int EnableDisplayDebug(IntPtr dm, int enable_debug);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int CapturePre(IntPtr dm, string file_);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int RegEx(IntPtr dm, string code, string Ver, string ip);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string GetMachineCode(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetClipboard(IntPtr dm, string data);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string GetClipboard(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetNowDict(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int Is64Bit(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetColorNum(IntPtr dm, int x1, int y1, int x2, int y2, string color, double sim);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string EnumWindowByProcess(IntPtr dm, string process_name, string title, string class_name, int filter);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetDictCount(IntPtr dm, int index);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetLastError(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string GetNetTime(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int EnableGetColorByCapture(IntPtr dm, int en);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int CheckUAC(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetUAC(IntPtr dm, int uac);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int DisableFontSmooth(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int CheckFontSmooth(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetDisplayAcceler(IntPtr dm, int level);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FindWindowByProcess(IntPtr dm, string process_name, string class_name, string title_name);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FindWindowByProcessId(IntPtr dm, int process_id, string class_name, string title_name);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string ReadIni(IntPtr dm, string section, string key, string file_);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int WriteIni(IntPtr dm, string section, string key, string v, string file_);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int RunApp(IntPtr dm, string path, int mode);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int delay(IntPtr dm, int mis);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FindWindowSuper(IntPtr dm, string spec1, int flag1, int type1, string spec2, int flag2, int type2);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string ExcludePos(IntPtr dm, string all_pos, int type_, int x1, int y1, int x2, int y2);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindNearestPos(IntPtr dm, string all_pos, int type_, int x, int y);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string SortPosDistance(IntPtr dm, string all_pos, int type_, int x, int y);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FindPicMem(IntPtr dm, int x1, int y1, int x2, int y2, string pic_info, string delta_color, double sim, int dir, out object x, out object y);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindPicMemEx(IntPtr dm, int x1, int y1, int x2, int y2, string pic_info, string delta_color, double sim, int dir);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindPicMemE(IntPtr dm, int x1, int y1, int x2, int y2, string pic_info, string delta_color, double sim, int dir);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string AppendPicAddr(IntPtr dm, string pic_info, int addr, int size);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int WriteFile(IntPtr dm, string file_, string content);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int Stop(IntPtr dm, int id);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetDictMem(IntPtr dm, int index, int addr, int size);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string GetNetTimeSafe(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int ForceUnBindWindow(IntPtr dm, int hwnd);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string ReadIniPwd(IntPtr dm, string section, string key, string file_, string pwd);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int WriteIniPwd(IntPtr dm, string section, string key, string v, string file_, string pwd);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int DecodeFile(IntPtr dm, string file_, string pwd);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int KeyDownChar(IntPtr dm, string key_str);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int KeyUpChar(IntPtr dm, string key_str);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int KeyPressChar(IntPtr dm, string key_str);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int KeyPressStr(IntPtr dm, string key_str, int delay);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int EnableKeypadPatch(IntPtr dm, int en);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int EnableKeypadSync(IntPtr dm, int en, int time_out);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int EnableMouseSync(IntPtr dm, int en, int time_out);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int DmGuard(IntPtr dm, int en, string type_);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FaqCaptureFromFile(IntPtr dm, int x1, int y1, int x2, int y2, string file_, int quality);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindIntEx(IntPtr dm, int hwnd, string addr_range, int int_value_min, int int_value_max, int type_, int step, int multi_thread, int mode);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindFloatEx(IntPtr dm, int hwnd, string addr_range, Single float_value_min, Single float_value_max, int step, int multi_thread, int mode);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindDoubleEx(IntPtr dm, int hwnd, string addr_range, double double_value_min, double double_value_max, int step, int multi_thread, int mode);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindStringEx(IntPtr dm, int hwnd, string addr_range, string string_value, int type_, int step, int multi_thread, int mode);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindDataEx(IntPtr dm, int hwnd, string addr_range, string data, int step, int multi_thread, int mode);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int EnableRealMouse(IntPtr dm, int en, int mousedelay, int mousestep);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int EnableRealKeypad(IntPtr dm, int en);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SendStringIme(IntPtr dm, string str);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FoobarDrawLine(IntPtr dm, int hwnd, int x1, int y1, int x2, int y2, string color, int style, int width);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindStrEx(IntPtr dm, int x1, int y1, int x2, int y2, string str, string color, double sim);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int IsBind(IntPtr dm, int hwnd);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetDisplayDelay(IntPtr dm, int t);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetDmCount(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int DisableScreenSave(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int DisablePowerSave(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetMemoryHwndAsProcessId(IntPtr dm, int en);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FindShape(IntPtr dm, int x1, int y1, int x2, int y2, string offset_color, double sim, int dir, out object x, out object y);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindShapeE(IntPtr dm, int x1, int y1, int x2, int y2, string offset_color, double sim, int dir);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindShapeEx(IntPtr dm, int x1, int y1, int x2, int y2, string offset_color, double sim, int dir);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindStrS(IntPtr dm, int x1, int y1, int x2, int y2, string str, string color, double sim, out object x, out object y);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindStrExS(IntPtr dm, int x1, int y1, int x2, int y2, string str, string color, double sim);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindStrFastS(IntPtr dm, int x1, int y1, int x2, int y2, string str, string color, double sim, out object x, out object y);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindStrFastExS(IntPtr dm, int x1, int y1, int x2, int y2, string str, string color, double sim);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindPicS(IntPtr dm, int x1, int y1, int x2, int y2, string pic_name, string delta_color, double sim, int dir, out object x, out object y);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FindPicExS(IntPtr dm, int x1, int y1, int x2, int y2, string pic_name, string delta_color, double sim, int dir);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int ClearDict(IntPtr dm, int index);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string GetMachineCodeNoMac(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetClientRect(IntPtr dm, int hwnd, out object x1, out object y1, out object x2, out object y2);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int EnableFakeActive(IntPtr dm, int en);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetScreenDataBmp(IntPtr dm, int x1, int y1, int x2, int y2, out object data, out object size);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int EncodeFile(IntPtr dm, string file_, string pwd);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string GetCursorShapeEx(IntPtr dm, int type_);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FaqCancel(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string IntToData(IntPtr dm, int int_value, int type_);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string FloatToData(IntPtr dm, Single float_value);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string DoubleToData(IntPtr dm, double double_value);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string StringToData(IntPtr dm, string string_value, int type_);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetMemoryFindResultToFile(IntPtr dm, string file_);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int EnableBind(IntPtr dm, int en);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetSimMode(IntPtr dm, int mode);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int LockMouseRect(IntPtr dm, int x1, int y1, int x2, int y2);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SendPaste(IntPtr dm, int hwnd);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int IsDisplayDead(IntPtr dm, int x1, int y1, int x2, int y2, int t);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetKeyState(IntPtr dm, int vk);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int CopyFile(IntPtr dm, string src_file, string dst_file, int over);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int IsFileExist(IntPtr dm, string file_);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int DeleteFile(IntPtr dm, string file_);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int MoveFile(IntPtr dm, string src_file, string dst_file);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int CreateFolder(IntPtr dm, string folder_name);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int DeleteFolder(IntPtr dm, string folder_name);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int GetFileLength(IntPtr dm, string file_);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string ReadFile(IntPtr dm, string file_);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int WaitKey(IntPtr dm, int key_code, int time_out);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int DeleteIni(IntPtr dm, string section, string key, string file_);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int DeleteIniPwd(IntPtr dm, string section, string key, string file_, string pwd);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int EnableSpeedDx(IntPtr dm, int en);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int EnableIme(IntPtr dm, int en);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int Reg(IntPtr dm, string code, string Ver);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string SelectFile(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string SelectDirectory(IntPtr dm);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int LockDisplay(IntPtr dm, int lock1);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FoobarSetSave(IntPtr dm, int hwnd, string file_, int en, string header);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string EnumWindowSuper(IntPtr dm, string spec1, int flag1, int type1, string spec2, int flag2, int type2, int sort);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int DownloadFile(IntPtr dm, string url, string save_file, int timeout);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int EnableKeypadMsg(IntPtr dm, int en);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int EnableMouseMsg(IntPtr dm, int en);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int RegNoMac(IntPtr dm, string code, string Ver);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int RegExNoMac(IntPtr dm, string code, string Ver, string ip);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int SetEnumWindowDelay(IntPtr dm, int delay);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern int FindMulColor(IntPtr dm, int x1, int y1, int x2, int y2, string color, double sim);

            [DllImport(WrapperLibraryName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
            public static extern string GetDict(IntPtr dm, int index, int font_index);
        }
    }

    public class DmException : Exception
    {
        public int ErrorCode { get; private set; }

        public DmException(int errorCode)
        {
            this.ErrorCode = errorCode;

            // 0 表示无错误.
            //- 1 : 表示你使用了绑定里的收费功能，但是没注册，无法使用.
            //- 2 : 使用模式0 2 4 6时出现，因为目标窗口有保护，或者目标窗口没有以管理员权限打开.常见于win7以上系统.或者有安全软件拦截插件.解决办法: 关闭所有安全软件，并且关闭系统UAC,然后再重新尝试.如果还不行就可以肯定是目标窗口有特殊保护.
            //- 3 : 使用模式0 2 4 6时出现，可能目标窗口有保护，也可能是异常错误.
            //- 4 : 使用模式1 3 5 7 101 103时出现，这是异常错误.
            //- 5 : 使用模式1 3 5 7 101 103时出现, 这个错误的解决办法就是关闭目标窗口，重新打开再绑定即可.也可能是运行脚本的进程没有管理员权限.
            //- 6 - 7 - 9 : 使用模式1 3 5 7 101 103时出现,异常错误.还有可能是安全软件的问题，比如360等。尝试卸载360.
            //- 8 - 10 : 使用模式1 3 5 7 101 103时出现, 目标进程可能有保护,也可能是插件版本过老，试试新的或许可以解决.
            //- 11 : 使用模式1 3 5 7 101 103时出现, 目标进程有保护.告诉我解决。
            //- 12 : 使用模式1 3 5 7 101 103时出现, 目标进程有保护.告诉我解决。
            //- 13 : 使用模式1 3 5 7 101 103时出现, 目标进程有保护.或者是因为上次的绑定没有解绑导致。 尝试在绑定前调用ForceUnBindWindow.
            //- 14 : 使用模式0 1 4 5时出现, 有可能目标机器兼容性不太好.可以尝试其他模式.比如2 3 6 7
            //- 16 : 可能使用了绑定模式 0 1 2 3 和 101，然后可能指定了一个子窗口.导致不支持.可以换模式4 5 6 7或者103来尝试.另外也可以考虑使用父窗口或者顶级窗口.来避免这个错误。还有可能是目标窗口没有正常解绑 然后再次绑定的时候.
            //- 17 : 模式1 3 5 7 101 103时出现.这个是异常错误.告诉我解决.
            //- 18 : 句柄无效.
            //- 19 : 使用模式0 1 2 3 101时出现,说明你的系统不支持这几个模式.可以尝试其他模式.
        }
    }

    #region structures

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public RECT(int left, int top, int right, int bottom)
        {
            this.Left = left;
            this.Top = top;
            this.Right = right;
            this.Bottom = bottom;
        }

        public override string ToString() => $"RECT{{ X1={Left}, Y1={Top}, X2={Right}, Y2={Bottom} }}";
    }

    #endregion
}
#endregion
#region namespace Xy.Licensing
namespace Xy.Licensing
{
    using System.Net.Http;
    using Microsoft.Win32;
    using Newtonsoft.Json.Linq;
    using Xy.Logging;

    public static class LicenseChecker
    {
        public static string GetMachineGuid()
        {
            string location = @"SOFTWARE\Microsoft\Cryptography";
            string name = "MachineGuid";

            using (var localMachineX64View = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey rk = localMachineX64View.OpenSubKey(location))
            {
                if (rk == null)
                    throw new KeyNotFoundException(
                        string.Format("Key Not Found: {0}", location));

                object machineGuid = rk.GetValue(name);
                if (machineGuid == null)
                    throw new IndexOutOfRangeException(
                        string.Format("Index Not Found: {0}", name));

                return machineGuid.ToString();
            }
        }

        public static bool CheckActivation()
        {
            const string GetLicenseDataUrl = "https://spreadsheets.google.com/feeds/list/1Gog92gelQrFV9IT7c-VKGZ2VG5Eq0Kk55e8fHEcvpMA/2/public/full?alt=json";
            var json = new HttpClient().GetStringAsync(GetLicenseDataUrl).Result;
            var data = JObject.Parse(json);

            return data["feed"]["entry"]
                .Select(x => (string)x["gsx$机器码"]["$t"])
                .Contains(GetMachineGuid());
        }

        internal static void ThrowIfNotActivated()
        {
            Logger.Instance.Warn("正在获取激活信息...");
            var isActivated = CheckActivation();

            if (!isActivated)
            {
                var machineGuid = GetMachineGuid();
                Logger.Instance.Fatal("本机尚未被激活!!!");
                Logger.Instance.Error("机器码: " + machineGuid);
                Logger.Instance.Error("请联系Xy来激活~");

                throw new InvalidOperationException("软件激活失败. 机器码:" + machineGuid);
            }

            Logger.Instance.Info("欢迎使用XyEF.Automation.");
        }

        private static readonly TypeNamedLogger Logger = new TypeNamedLogger();
    }
}
#endregion
#region namespace Xy.Logging
namespace Xy.Logging
{
    using System.Runtime.CompilerServices;
    using Splat;

    public class ConsoleLogger : ILogger
    {
        public LogLevel Level { get; set; }

        public ConsoleLogger()
        {
            this.Level = LogLevel.Info;
        }

        public void Write(string message, LogLevel level)
        {
            if ((int)level < (int)Level)
                return;

            Console.WriteLine(message);
        }
    }

    /// <summary>Logger that writes stylized and timestamped message into LINQPad result panel</summary>
    public class StylizedConsoleLogger : ILogger
    {
        public LogLevel Level { get; set; }

        private IDictionary<LogLevel, string> styles;

        /// <param name="styles">Use css for style</param>
        public StylizedConsoleLogger(IDictionary<LogLevel, string> styles = null)
        {
            this.Level = LogLevel.Info;

            // default styles is color coded based on level and monospaced
            this.styles = styles ?? typeof(LogLevel).GetEnumValues().Cast<LogLevel>()
                .Zip(new[] { "Green", "Black", "Orange", "Red", "DarkRed" }, Tuple.Create)
                .ToDictionary(x => x.Item1, x => string.Format("color: {0}; font-family: 'Lucida Console', Monaco, monospace;", x.Item2));
        }

        public void Write(string message, LogLevel level)
        {
            if ((int)level < (int)Level)
                return;

            Util.WithStyle(
                string.Format("> {0:yyyy-MM-dd HH:mm:ss.fff}: {1}", DateTime.Now, message),
                styles[level]
                ).Dump();
        }
    }

    /// <summary>Cached logger that writes log message on behalf of the given class.</summary>
    /// <remarks>Static class are also support.</remarks>
    public class TypeNamedLogger
    {
        public IFullLogger Instance { get { return instance.Value; } }

        private readonly Lazy<IFullLogger> instance;

        /// <summary>Create a logger named after current method's declaring type.</summary>
        public TypeNamedLogger()
        {
            var frame = new StackFrame(1);
            var type = frame.GetMethod().DeclaringType;

            instance = new Lazy<IFullLogger>(() => GetLogger(type));
        }

        /// <summary>Create a logger named after the given type.</summary>
        public TypeNamedLogger(Type type)
        {
            instance = new Lazy<IFullLogger>(() => GetLogger(type));
        }

        private static IFullLogger GetLogger(Type type)
        {
            // taken from LogHost.Default.get
            var factory = Locator.Current.GetService<ILogManager>();
            if (factory == null)
                throw new Exception("ILogManager is null. This should never happen, your dependancy resolver is broken.");

            return factory.GetLogger(type);
        }
    }

    public static class ExceptionExtensions
    {
        public static TException BindContext<TException>(this TException exception, IDictionary context)
            where TException : Exception
        {
            foreach (DictionaryEntry entry in context)
            {
                exception.Data[entry.Key] = entry.Value;
            }

            return exception;
        }
    }
    public static class LoggingExtensions
    {
        public static void LogCurrentMethod(this IFullLogger logger, LogLevel level, [CallerMemberName]string caller = null)
        {
            new Dictionary<LogLevel, Action<string>>
                {
                    { LogLevel.Debug, logger.Debug },
                    { LogLevel.Info, logger.Info },
                    { LogLevel.Warn, logger.Warn },
                    { LogLevel.Error, logger.Error },
                    { LogLevel.Fatal, logger.Fatal },
                }[level](FormatHeader(caller));
        }
        private static string FormatHeader(string message, char padding = '=', int totalLength = 80)
        {
            if (message.Length > totalLength)
                return message;

            const int TwoSpaces = 2;
            var length = totalLength - message.Length - TwoSpaces;
            return string.Join(" ",
                new string(padding, length - length / 2),
                message,
                new string(padding, length / 2));
        }
    }
    public static class PrettifyingExtensions
    {
        public static string Prettify<TKey, TValue>(this IEnumerable<KeyValuePair<TKey, TValue>> pairs, string itemSeparator = ", ", string keyValueSeparator = "=")
        {
            return string.Join(itemSeparator, pairs
                .Select(x => x.Key + keyValueSeparator + x.Value));
        }
    }
}
#endregion
#region namespace Xy.Reactive
namespace Xy.Reactive
{
    using System.Reactive;
    using System.Reactive.Linq;

    public static class ObservableExtensions
    {
        public static IObservable<Unit> CastToUnit<T>(this IObservable<T> source)
        {
            return source.Select(x => Unit.Default);
        }
    }
}
#endregion

namespace EOF {