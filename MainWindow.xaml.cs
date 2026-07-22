using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SystemOptimizerLite;

public partial class MainWindow : Window
{
    private readonly List<CategoryInfo> _categories = new()
    {
        new("系统优化", "轻量、安全、可回退的系统优化工具"),
        new("系统清理", "基于 BleachBit 的系统垃圾清理与空间释放服务"),
        new("驱动管理", "检查驱动状态及安装官方驱动中心"),
        new("工具箱", "实用 Windows 系统专属工具"),
        new("设置", "主题、本地组件与运行目录")
    };

    private readonly OptimizerService _optimizer = new();
    private readonly CleanerService _cleaner = new();
    private readonly DriverService _drivers = new();
    private readonly ToolboxService _toolbox = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly HashSet<string> _selectedCleanerIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _busyOptimizerIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _optimizerOperationText = new(StringComparer.OrdinalIgnoreCase);
    private AppSettings _settings;
    private string _active = "系统优化";
    private int _renderVersion;
    private int _topStatusVersion;
    private CleanerRunSummary? _lastCleanerSummary;
    private HashSet<string>? _lastCleanerPreviewIds;
    private bool _cleanerSelectionInitialized;
    private bool _forceDriverRefresh;
    private bool _optimizerComponentBusy;
    private bool _cleanerComponentBusy;
    private bool _cleanerRunBusy;
    private DownloadFailure? _lastDownloadFailure;
    private const string HelpDocumentUrl = "https://yangg-app.notion.site/system-optimizer-help?source=copy_link";
    private const string ManualDownloadUrl = "https://yangg-app.notion.site/system-optimizer-tools-download?source=copy_link";
    private readonly DispatcherTimer _hoverShowTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private static readonly TimeSpan HoverInitialDelay = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan HoverSwitchDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan HoverSwitchGracePeriod = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan HoverFadeInDuration = TimeSpan.FromMilliseconds(90);
    private static readonly TimeSpan HoverFadeOutDuration = TimeSpan.FromMilliseconds(80);
    private string? _pendingHoverText;
    private Point _lastHoverMousePosition;
    private FrameworkElement? _currentHoverTarget;
    private bool _isHoverInfoVisible;
    private bool _recentVisibleHoverClosed;
    private DateTime _lastVisibleHoverClosedAt = DateTime.MinValue;
    private int _hoverGeneration;
    private Func<Task>? _drawerPrimaryAction;
    private Func<Task>? _drawerSecondaryAction;
    private IInputElement? _drawerPreviousFocus;
    private readonly Dictionary<string, PageCacheEntry> _pageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _pageRenderVersions = new(StringComparer.OrdinalIgnoreCase);
    private HwndSource? _windowSource;
    private const int RoundedWindowRadiusDip = 16;
    private const int WmDpiChanged = 0x02E0;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmBorderColor = 34;
    private const int DwmRoundCorners = 2;
    private static readonly IReadOnlyDictionary<string, Geometry> NavigationIconGeometries = new Dictionary<string, Geometry>(StringComparer.Ordinal)
    {
        ["系统优化"] = CreateOriginalNavigationGeometry("H4sIAAAAAAAEAG1XSZLkIAz8ih9QTSCxSJwdMafqD/XrJ8Fmpy62hdCSSgnqH/06k5yEzw/dzgQfVbxECkqeIbucIXEkUWLwnjRBZqwkz2Kt02i9XGyUoreRsxHDTlmJA8Q+pOgCBxG2qSy6JOSCk5icZQ8V8lkwqkSyUbpxMiqaqvH6fq16n5/V1rV6yypzRFePNi/WLK41w8/PisJFWTaj9cgyll+8EZts635efXB0NIw15ajMrFbV6RQSwa26kE0esqEcgHXRcwkqHTCBigbWUWVA0xtissX6oRDeOE2uB3YopzfRjtYnIngDYIQU1g8c8iaxDdKsH6gHlfKzPjo4vRpPge8Dbn6/G7qHLSu6B8cDxnPQK7yHfFd4D6gNIM+Ir5qnYi3+TiXvAS90WfI8MW1BayJqaAz+5vcH+3vfsjfB5njvnTHose22fPeG3VDb235CfJgYe7G2WbOXfJ1YM136sDswbR2TI1GvzuC/6zd/uLcrOROaU/KiwdUcs3rw2kAs0NjgnA55s8EWCo8VQ9GW9xyxZSYHgw0yjjHxlDc7m+tYtnrqW+t7lgdN6t3bGu97lgOblzXOiHNWxuokEWAeBlfq6la4Jd9MJh8jdejhOxAAlV7YLETUga2Eaq8IU7TP3PUPbR8w7/dzQvPgaQV3DnLA9ZDgiu6MzQDsDObwMaM/bD9UbvU/F30I+0CYNe9r5xn5a6Qi+W+DNn/dK/plx1qng6de5znIlR2HBDu7Zmx4RrmByTPIDf2V84fK9Z6Zi7522oEwfOAZj1T88kDTmw9E3r1sDTAGOLTOntrWdTz1cetXntp4wLZjziO0a7W2QTIWehhBO0W26bVTi6+BfIwpqaYG5W/FLPcUoy1gC+jtL0XYvh0NRQb4RXCmVC9qYhveOCbIPcNbp3NHy05SVUvtzFJUvFf3MY7EFWSlrsLOv7YfL9/43lmSeQ6hgJRKn4t5fN+QIOZEwVZsxOTvGKsEm1TqgSY47pK4VEPFYh8AYmytYTAxJabXly3UbETCovWDB6hAlh9o+PxAp+MUDIozvpajFNfDc+glguEMSrK2VbGVX+orhLNWybBYChU6VwApHu0kC88DVwDEG7qb3aIERlDnSGonnzJIIdOuR7IlXu6meHyDeWHWK5o6knCHkFLSG7J8l9CaQFkUKYzAhsggouv0waIGMDPV8RNNKzLWUu06iMstIoxmU24WqtMnmsxVHlUYwlZ/fKEafbThi22LP+VJ/ZYi5aWXoTnAl0yoTT0SIMzjuU056XcaZOtRFLJ9qQ5ooFSueN9kim5mNM4oiyngaz8IKF0ObUjznc+1YwkFtEkHukAFt02dVSS6oV+hwr7xP2ECPNc0i74LreCpLBbq9HuUxcgDIm8hU643ZIUCefDjma9oDyVtDeA5irOhMaiFXPmS2Hu1XSp40ksHNtOpC+jtEDp1z8no0nRzNO0idkhj6fI9+3K5LfMDSD3/XeVKxldu5FlZGuFOoBLm3TRHqfCmmYdKKx0Wp0mFxf732JcrNEQ0DF70wTiUk8n/RSop60HRDpK//5QXPF0AEQAA"),
        ["系统清理"] = CreateOriginalNavigationGeometry("H4sIAAAAAAAEAIVXWbLcOgjdSi8gV4UkxPDdVfnqbOit/oFmuZVKUnXdFhjD4TD4d/wTSyi/fmIO5T1/ZikYCTKIZHnZMTILYaKCGLWqJMXEUGI9qSo5F83cDCRTpVT8PIvmlJJUW1UYi0jJZVnPGThJP2gqIHbUH3KVVFhiasaBMUXhaj0KipbuRpMi5OlqVQGLZQTTVKKk+T5XgejeNhnUF70iBrUAJQFFJiYTpqDKeb7NVcRjtldRNge7SvcNQ3fzeUwKEfKK9hRmgQQrHhdmC3HGg6HIwNmFBH5Tz3NesHapkPJEEQP6/Q50PcSSO7Qfhx4Fm/nAnP3n208jR9kCDSVV2asibnBrmTgZNzwFBbIQIL/ck6TUoNiyl4LEskMBQYrnXZg0Q8r1USJdRHMdsruHDmpHGkKpnjbz+Wk+l2Q0LpSRTE9cJ8a4QwJGXae60S0TJVOJQSQ17kGImbr1GKh4kJtjYHecNLKCQQjRlTAiOLXAbyL1m09njrkM75VReGEw13JBYyEn0HI1i6HHeHiUzc/FHdFLLDnQVi5N5QmJ9QHgEe4EM7ca1d2vZx5yL0YuLe+3fHrxQmfbYoLxo4a4rH9xKK0sLO7ZYRmVO8j6MbHRtJEtv+0OklmIqbqAjfTBDJcEy9PvYglZRx3sZdbzs5n7qtDeGh/GO+dDujUFa4ke7F8eAcsMPF+5NyIIyvTVwaATYljFS+8zHXDyWU8ye7l0C94wZ/uHwHli7wns1ol4FLX3Byxi5BwOUQRihkGrpkN0mC3U66FOjJZSO00pWs0OyrR5ZGHMTuw67VTtaYTWk+Zpmb9qovb3oSJrx7Fb5mLsyAsOmt0vD3I5AOhwn0BqpdBKgVeRmCdLB1/R/35sqg5C4ttu1IpEYNagteBagjLQK4FrlrjMcrAeYS9bABdzbhkNLREvstyrZaJRsdouj2RSiEr0UDGn1YLRBiRZ05qZXXPdzimvUc7tUYrZ7tejsm0OXeWEmMOo2DIB5uAFM6LrTp34cnCFvKvQS9pfK7gVIndRqD3z9OQ0KWGkenmiB5HlFoCGTMZt+7+pnDCodfQ02tyGoFq12ZBG3hE8wVcrbROeKmcKozX9NbNn8u04qcThmF5YYyoI3krWMvTknqtM05/YOOtL1F5Ljdvu+gbMpSh8tStrtRogOyS73qUQ5yZ61vDaVf97/aFtE8KPzlHrHnuS1lznBkYsW+yGdBIdD9hv5qIsQxZ1AWzPTHbMTdH3rWQduKNtbNKxhDzXOGca8ZRx341lWxDxXIVMRrV/lr1L1AkY+w5mKtiGUpX1BctO96WnB16nw/ZgpIiPDqT13+ilnkwvS6kfAnDYOhT5a6P4eh2HkhfOw08OtZfsI/8ZIG9L/xMf6yI5TdlAlMfsu+TAWtMc6M/cmQxWM53ppsBxIXwyhA7zciPYwU/jq9F2jJZPCqMqfG8b+3Zfcd52xGgos6Eo1qtdRiltI8ieUvGNbJsRy3HzZM1m2zj68oJ92dt6f4DeqExoxssR0GttqT/xs+2vP/Ftd6t86qILcnx9BNjnOLpKX9uqsI/+ukc/PnmsBVfuz88H2+0s9NnGqsrcwtdW2KzVUbA1qtB4UkXysLI+RGb6Qy37+mHw+GwMw2MTDRisa/WvxTTH2siid6hfPwbrxy42Ed71svczduZBXUpWGejRCavK/CLT2ULJsro2QM+WHr1X/LlMj1EGzsl+PR3ph+err0bnxr07c2pdguBL7PWsfLiC9K6X0+OLYXzEvTuEA6hLIPjA+QIANnDwAhxe8L5YnHnaPXnm9zuCW+AVE2OQNgZpY5BeUNT2BtmT9gBfbwzSfzNIbwzSzqCnI/3wnwzSG4OeWpcgIlyCb4f2zQeNRO36APPb+FcO9Mai72C+cv4NAjaA8AIeXjC/WLyw6DvH3xHcAm8s+h8XjmiT5BMAAA=="),
        ["驱动管理"] = CreateOriginalNavigationGeometry("H4sIAAAAAAAEAI2YW7LkIAhAt5IFzHThC+W7q+arZ0Oz+hExiQK59/7EDrERTsJD/4S/If/6TR++1iOUV+nju4+YSg4ICVpLbT6rJXZJOWX4ogIlQgkl59CIp7R+g7Hww/mTpes84merptr/iCxcl7yEZY4Jc2kJlqWM0phrovJgTcgtp1pcNyBTrgRQawQaU5T3OMng4NTv+jVPWXZoZWeZNA2uJTWEXDf70he0kmjKt33IQlmy7MJzFFpxWcooFVoP1jzTSt/TSpNM+oRue2JF/ebdb0o34XyJ3Yj4QooV2mnnrz4lQY0ULl3xlftit599CgsjsqUfvoFXecsoTqeQMpYQ2hGi9hqm1y22hBjHlMtruL2O2muYXgfsnvdJEHjS7jewqMiwfq0kovXrpCeN0wJlzJjZFmOMF777A86Hx3gEOK8DGHj/IBXQzlLk8SIVz653pOK7U6GBi1RsD9Eeyw8KHVq0waquC67rwoo/yqPOC4Oq3mSd+JxF0OOk857rls6DHQcOTjoHDtEPOKHHSSc9zwXXdeGEjCjPC3PK3mSd8pxFksdJZzzXLZ0BO470kuue/YZoz3YPCh1OOt15LriuC6clWb1NtrPJzQhsijzVcXqFkWXfZnVOwdpMJ3PD7fCd8S0Yp1iAU2NA6g84tcnXqaraZs1ZDT0vVBl1vM/HIJM/kX9HrthDgB4qdBYxLcFiHH6JynQh4DQvII0NOE2Pr1O1S5s1z6hMf+Z4X4VMZVSVUZEIyENFziI64a7G0ZeodJJnHroWiKzI8ANUuhLt1jyj0pXPQxVA0ARgWP2Or3HKosdrPNVL6Xq+mnh3AK53uotgLLrZmLIyx61FeVK6NTvKnmdmur1ymaXJJ33uxBXS2yY60/rZdGkayDRk0mFyquW28y3j7rfN4rqxXbP/3Qo7hUO30rbc/A44KpGMqkiJUJc3R+lVFjdrdD01bmTHe5GVD4+ZK/i8Mi105tvewCyDLi3bjhjHTBPDQHDSMq2PCHXT5Cj1aNkuzbhRHe/rpFUHJ7quTIuc+bbjNMuQS8s2ucYx0xozEJq0TEMtwm9pkUvL9v7GDd40GPensG8zYKDi3cY5MLL5Q/3J7GnsYtGFZjdSxj+z/WIuY2s2f6jtqgjVhs/T6mGze0zriZeRplC2sFcSe9sNsEl6dhtt/rRsu3vilc14T7p2px6Ss8M3eX09Glgqgj1UMKWEDyNMEZrCcv7Yy5erda96yp7nswpTah0CcR5WhDjOdvg+j+o+xeBhA2cx00KsZtIX1EzPwlxMtyPCMsdvT8NMc7Vb88zM9HPWfZps6N/xl8O7f1n8FgBjhoy9ENZxBtRfWy/fKQKGinUcSwGXvXCfWfWXTzCPnKC2IAFVAmzHSa+QIN6NwAjhUFrrVl5TIoQWYy235goVUhDdkSJhmyc71EItLVxGpZZDyAVu7Q2pLiGYXrnRdpzWIySGJlsVBJrHNDT6oyssexD2mMTliC7J1kbmJ96gjOiPbxm6nypJKo2MFHOKMbZ7yjSEWTasYXqpXGCqQN3N3oOd6UyB0FR3hsuNw19rO/R6kjhWm47FYMk58bR981GerRwOTUryWBzJLo9Md1IeT/R7cDSeb3E1RL91xwX97Tgg7m9vZ6i/WIe//u7lLa6xcZxRI69/hNKho0w+mzUSj/m1rRErMm6i83v9TK02xlkb3gFzG8EYS0ahp41nnlymrhhlBHs323nWup4J9CntShDz56FnSfhvZwJ6LSeJ3LYuyUf75uSs+9NK3Finf/8BMQaRWEMZAAA="),
        ["工具箱"] = CreateOriginalNavigationGeometry("H4sIAAAAAAAEAG1YSZLlKgy8CgeoIpgF6xfxV9UX6tP/1IDArn6LKluSQWhIpf1f/pO+vkscpX/kYtHKY/REVNLqIcU0RktQf33XWHPPqaWjzGuVPEmUo5aZC0trLanmUlJeNGiycvXUS6Je50iNYNNLbZUfbFGvICNyWZt89TNi1quv7xVnX7j6QJYGUeq5t5bn+vrO8LLB65FqmrPOMGKiPHIr4zIpheYw0WKTVtYQ36FsmeSG5blV2HY9pGhHmolKmba6yGitRrPXW2bRkLscW+9UjkVMfc3VL7dzXAnxzMdt8wK6Ei2cIxbeae2oi446ojlT3mdpg1qjy+cac6+j2W5sMmaV+InSog/xXIvqugJVIcL53W/CShN3ek9swilLTcJF2DrZsi3CKnfUCsQ0aip3jlqcdUkuw4yJI2H+i7Yjqan0RRbjGZsFoqPqtAIhhKrveJE+12vBKcnSuuDaWDsvZkKJi2+HYkWvaOimlGoPXEMrZ4+YKFdDfuqOBEwkpjvuMBkhZ/sXdxjkBsWf3Ctdy2oDykpzuQNoEQkZ5L0ihTsEqtTgspLSGrU9lKaZq3U+gwolUiiRRAvFfnsgu5dYxjzpVUXl364mmJycInhShRAOPnzrA6eioarCWLGrFyZT1i6Xj0N+l8ma6WS26xFQrLXl3a6WVtT35VOF29zj6XK71eb9JCZe4KiZ6msjYed0orxLZLIJ4G7uAOZC48r5U5mSheMWo7Kp1Icn7z3bq2NvZ1uUAvhhTMk77gWxUpjJVgFmJh2qz37ey4rusXV4O6fdv0/hB3qLs4LPW1wk5CdNv3bjwC8DbPczSK4GhsGpjFc+wzvjCmEGgbtSBPq8X17FJUrv5ldxinI0yt5BYZe2qFonf/DRDaItYyxvpaCdJIpHj4XdfYLsuymDNaoIHz0cTn+L8tH94SCDKDdkhIMkIn/gTMg6eKxi5Eb/YSYnDsgV7jl41Ht8gIqdHEinnGY7tzC6ThFI152SA1I3UAJznKTk3EOKKyt2y3Sw8PjQYGmallt6dFfnYemVhvHFzCM7L+FCaHuXEavDXm0Fk9ALDSOV55CPGsZLJJOunlEMHwzv8w6SyQaPv4O7b37AJq10K6XDKlheah90IPjNSmTmGBi+SU3YdEdUxoY2Lfr6Bi5IbXxatHttCkzmzo11yAJmVtdR3WLOpAFLngAws5kfZAqlMsQMOuSz3OQAR5o24etFKCA2FwNTC6Yrh4qAJDZrBQACMfTeyjTXfrLkczwvuIpL4U1JqaMo7SA4AiCijIDGN9RJXkQgSU3r45YJDB0udauEJMzSr31059lnHhACk+PwCOa50Q2Yx6xklLFQa01CmLPsM4jZCTKNREucr1O0hZLqzW24/DYaI784lwSaE8gbVJLlqzy7kBEUMcAEfZEnc+4uxbtXQ8+j/HjHi3OfAGevimRFeU7J2kk0bqLPbwabptvrwt/wZ0XabV50TH0gogPjXDOu45JWeBah4i1kwxfJ1jepW1fQr+7FIZpjS5rN4ZSWduLaTx7I4h72PVI+WK4LznRoOfsrXG4nQu56f1sw9JsBGtdCCWk3fHPKCiiTFm/4i8QQK3vR9xwhCsxN08xGqijOXHHc4x834Y7axPtBt0Ohm9eB3cnKe4AoDjd6zMB5+CTYxFRo383JTAG1js02w1txbSxjSOefn4JjLaufiaLT5rdlhqcX4IrJBDB2PzXPt1n2uZbNYh2Key5DXrDPRYAxTk8wpnTgmfZ4tWqvTWfxcZ+ZbgEMFMtk+GlnfHja86p4zPxf3ir8IGf15HIdclQed9nR+yHciHQvmvfelxs/6wiPAZqrt/PSSfoKSY0u7gkTpR/8nuK8TKVl/0dXXdEK/1i26BxxGnz743H70UgqA5bgft5xV92dm3DyJspHVsPJuCo9/V4hKr/rJ7wrTE3uKtS3NJ3Vd9X+w9KLXl1nFpcgMF3zN63m8y/Mi8ZxzwG4+37k3ZVikudFhsPpan37NoALbyxQvj2KT+QVDpaIMhkNOvgj6dqldqDKyKvGVntU3iO8YEQWtM9nemheCKrfR5i8OA8+2Ks4YewovAFbv8q0dOhVeAO+fts5/lvz2MwIe4yIzCaKzxsWPofRS7eXOCJ3VCfi8WI9XnRkpWTIoUXwk5F4o+t1f8j6oGpLv757yWcyTmD3qYwxzS7WcplojR6uq10GgtSFSPCXrkM2mCDxEsVX5AGtnCrbrNaLlp4U+voChkZ2oo7d/PMEmmNPjAd7mE7V98c+EY+y31LAVP0936/DsWDxfjLki/mcfcLZncXbq3B8ZbHz7ff5oAxyeL3Yrod3vHSRO6ZMRW1L5p/PbIiHj4yFd07lbI+8h3q+bXqh/Cz/BqDV9HlXmbCLZyXuGr141ddvGvb3f1jX1evXFQAA"),
    };
    private static readonly Geometry DefaultNavigationIconGeometry = CreateOriginalNavigationGeometry("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");

    private sealed record DownloadFailure(string Page, string Module, string Reason, string TargetDirectory, string Instruction);
    private sealed record PageCacheEntry(List<UIElement> Content, List<UIElement> Actions, double ScrollOffset, DateTimeOffset CachedAt);

    public MainWindow()
    {
        InitializeComponent();
        _hoverShowTimer.Tick += (_, _) => ShowHoverInfoNow();
        SourceInitialized += (_, _) => InitializeWindowRegion();
        SizeChanged += (_, _) =>
        {
            UpdateResponsiveGrids();
            ApplyWindowRegion();
        };
        StateChanged += (_, _) => ApplyWindowRegion();
        _settings = SettingsService.Load();
        try { CleanerService.RecoverInterruptedInstallation(); }
        catch (Exception ex) { LogService.Write($"BleachBit 中断安装恢复失败：{ex.Message}"); }
        AppPaths.CleanupTransientCaches();
        _drivers.ClearCache();
        _active = "系统优化";
        ApplyTheme();
        Loaded += async (_, _) =>
        {
            RenderNav();
            await RefreshTopStatusAsync();
            await SelectPageAsync(_active);
            _ = WarmDriverCacheAsync();
        };
        Closed += (_, _) =>
        {
            if (_windowSource != null) _windowSource.RemoveHook(WindowMessageHook);
            _shutdown.Cancel();
            _pageCache.Clear();
            _optimizer.InvalidateLiveStateCache();
            _drivers.ClearCache();
            AppPaths.CleanupTransientCaches();
            _settings.LastSelectedPage = "系统优化";
            SettingsService.Save(_settings);
            if (!_settings.KeepLocalComponents) AppPaths.CleanupRuntimeComponents();
        };
    }

    private async Task WarmDriverCacheAsync()
    {
        try { await _drivers.GetPageDataAsync(forceRefresh: true, token: _shutdown.Token); }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex) { LogService.Write($"驱动页后台预热失败：{ex.Message}"); }
    }

    private void RenderNav()
    {
        NavPanel.Children.Clear();
        foreach (var category in _categories)
        {
            var selected = category.Name == _active;
            var content = new Grid { Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.Children.Add(new System.Windows.Shapes.Path
            {
                Data = NavigationIconGeometry(category.Name),
                Width = 16,
                Height = 16,
                Stretch = Stretch.Uniform,
                Fill = selected ? BrushOf("Blue") : BrushOf("Muted"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                SnapsToDevicePixels = true
            });
            var label = new TextBlock { Text = category.Name, FontSize = 14, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, TextAlignment = TextAlignment.Left };
            Grid.SetColumn(label, 1);
            content.Children.Add(label);
            var button = new Button
            {
                Content = content,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Height = 44,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 0, 6),
                Background = selected ? BrushOf("BlueSoft") : Brushes.Transparent,
                BorderBrush = selected ? BrushOf("Blue") : Brushes.Transparent,
                BorderThickness = selected ? new Thickness(1) : new Thickness(0)
            };
            button.Click += async (_, _) => await SelectPageAsync(category.Name);
            NavPanel.Children.Add(button);
        }
    }

    private static Geometry NavigationIconGeometry(string page)
    {
        return NavigationIconGeometries.TryGetValue(page, out var geometry) ? geometry : DefaultNavigationIconGeometry;
    }

    private static Geometry CreateOriginalNavigationGeometry(string compressedData)
    {
        using var source = new MemoryStream(Convert.FromBase64String(compressedData), writable: false);
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        var geometry = Geometry.Parse(reader.ReadToEnd());
        geometry.Freeze();
        return geometry;
    }

    private async Task SelectPageAsync(string name)
    {
        var previousPage = _active;
        var samePage = name.Equals(previousPage, StringComparison.OrdinalIgnoreCase);
        var forceRender = samePage && ContentPanel.Children.Count > 0;
        var preservedOffset = samePage ? ContentScrollViewer.VerticalOffset : 0;
        if (!samePage && ContentPanel.Children.Count > 0)
        {
            if (_pageRenderVersions.ContainsKey(previousPage))
                _pageCache.Remove(previousPage);
            else
                _pageCache[previousPage] = new PageCacheEntry(ContentPanel.Children.Cast<UIElement>().ToList(), PageActionPanel.Children.Cast<UIElement>().ToList(), ContentScrollViewer.VerticalOffset, DateTimeOffset.Now);
            ContentPanel.Children.Clear();
            PageActionPanel.Children.Clear();
        }
        var renderVersion = ++_renderVersion;
        HideHoverInfo();
        _active = name;
        _settings.LastSelectedPage = name;
        SettingsService.Save(_settings);
        RenderNav();
        var category = _categories.First(x => x.Name == name);
        TitleText.Text = category.Name;
        SubtitleText.Text = category.Description;
        if (!forceRender && _pageCache.Remove(name, out var cachedPage))
        {
            ContentPanel.Children.Clear();
            PageActionPanel.Children.Clear();
            foreach (var child in cachedPage.Content) ContentPanel.Children.Add(child);
            foreach (var action in cachedPage.Actions) PageActionPanel.Children.Add(action);
            ContentScrollViewer.ScrollToVerticalOffset(cachedPage.ScrollOffset);
            StatusText.Text = $"就绪 · 缓存于 {cachedPage.CachedAt:HH:mm}";
            _ = RefreshTopStatusAsync();
            if (name == "系统优化") _ = RefreshCachedOptimizerIfStaleAsync(renderVersion);
            else if (name == "驱动管理" && DateTimeOffset.Now - cachedPage.CachedAt > TimeSpan.FromMinutes(30)) _ = RefreshDriversInBackgroundAsync(renderVersion);
            return;
        }
        _pageCache.Remove(name);
        _pageRenderVersions[name] = renderVersion;
        ContentPanel.Children.Clear();
        PageActionPanel.Children.Clear();
        StatusText.Text = "正在加载...";
        try
        {
            if (name == "系统优化") await RenderOptimizerAsync(renderVersion);
            else if (name == "系统清理") await RenderCleanerAsync(renderVersion);
            else if (name == "驱动管理") await RenderDriversAsync(renderVersion);
            else if (name == "工具箱") await RenderToolsAsync();
            else RenderSettings();
            if (!CanApplyPageUi(name, renderVersion))
            {
                CompletePageRender(name, renderVersion);
                return;
            }
            CompletePageRender(name, renderVersion);
            await RefreshTopStatusAsync();
            StatusText.Text = "就绪";
            if (samePage) ContentScrollViewer.ScrollToVerticalOffset(preservedOffset);
            else ContentScrollViewer.ScrollToTop();
            MotionService.FadeSlideIn(ContentPanel);
        }
        catch (Exception ex)
        {
            if (!CanApplyPageUi(name, renderVersion))
            {
                CompletePageRender(name, renderVersion);
                LogService.Write($"页面加载已过期：{name} - {ex}");
                return;
            }
            CompletePageRender(name, renderVersion);
            ContentPanel.Children.Clear();
            AddInfoCard("加载失败", ex.Message, true);
            StatusText.Text = ex.Message;
            LogService.Write($"页面加载失败：{name} - {ex}");
        }
    }

    private bool CanApplyPageUi(string page, int renderVersion) =>
        renderVersion == _renderVersion && _active.Equals(page, StringComparison.OrdinalIgnoreCase);

    private void CompletePageRender(string page, int renderVersion)
    {
        if (_pageRenderVersions.TryGetValue(page, out var currentVersion) && currentVersion == renderVersion)
            _pageRenderVersions.Remove(page);
    }

    private void UpdateComponentProgressIfActive(string page, int renderVersion, string message)
    {
        if (CanApplyPageUi(page, renderVersion))
            StatusText.Text = message;
    }

    private async Task RefreshComponentPageIfActiveAsync(string page, int renderVersion)
    {
        _pageCache.Remove(page);
        if (CanApplyPageUi(page, renderVersion))
            await SelectPageAsync(page);
    }

    private async Task RefreshCachedOptimizerIfStaleAsync(int renderVersion)
    {
        try
        {
            var items = await _optimizer.GetItemsAsync();
            if (_optimizer.TryGetLiveStateCache(items, out _, out var stale) && stale)
                await RefreshOptimizerStatesInBackgroundAsync(items, renderVersion, true);
        }
        catch (Exception ex) { LogService.Write($"缓存优化页后台刷新失败：{ex.Message}"); }
    }

    private async Task RefreshTopStatusAsync()
    {
        var version = ++_topStatusVersion;
        var page = _active;
        TopStatusPanel.Children.Clear();
        AddStatusChip(AdminService.IsAdministrator ? "管理员权限" : "非管理员", AdminService.IsAdministrator ? "Green" : "Yellow");
        var runtime = EnvironmentService.RuntimeText();
        AddStatusChip(runtime, runtime.Contains("正常") ? "Green" : "Red");

        if (_active == "系统优化")
        {
            var optimizer = await _optimizer.GetComponentStatusAsync();
            if (version != _topStatusVersion || page != _active) return;
            if (_optimizerComponentBusy) optimizer = optimizer with { State = ComponentState.Downloading, Text = "获取远端服务中" };
            AddStatusChip(optimizer.State == ComponentState.Online ? "Optimizer 在线" : optimizer.State == ComponentState.Downloading ? "获取远端服务中" : "Optimizer 离线",
                optimizer.State == ComponentState.Online ? "Green" : optimizer.State == ComponentState.Downloading ? "Yellow" : "Red");
        }
        else if (_active == "系统清理")
        {
            var cleaner = await _cleaner.GetStatusAsync();
            if (version != _topStatusVersion || page != _active) return;
            if (_cleanerComponentBusy) cleaner = cleaner with { State = ComponentState.Downloading, Text = "获取远端服务中" };
            AddStatusChip(cleaner.State == ComponentState.Online ? "BleachBit 在线" : cleaner.State == ComponentState.Downloading ? "获取远端服务中" : "BleachBit 离线",
                cleaner.State == ComponentState.Online ? "Green" : cleaner.State == ComponentState.Downloading ? "Yellow" : "Red");
        }
    }

    private void AddStatusChip(string text, string colorKey)
    {
        var border = new Border
        {
            Background = BrushOf("Panel2"),
            BorderBrush = BrushOf("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(9, 5, 10, 5),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = BrushOf(colorKey), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        border.Child = row;
        TopStatusPanel.Children.Add(border);
    }

    private async Task RenderOptimizerAsync(int renderVersion)
    {
        var status = await _optimizer.GetComponentStatusAsync();
        if (!CanApplyPageUi("系统优化", renderVersion)) return;
        if (_optimizerComponentBusy) status = status with { State = ComponentState.Downloading, Text = "获取远端服务中" };
        AddActionButton(status.State == ComponentState.Online ? "重新获取服务" : "获取远端服务", async () => await RunGuardedAsync(async () =>
        {
            ClearDownloadFailure("系统优化");
            _optimizerComponentBusy = true;
            await SelectPageAsync("系统优化");
            var componentRenderVersion = _renderVersion;
            try
            {
                await _optimizer.InstallAsync(_shutdown.Token, msg => Dispatcher.Invoke(() => UpdateComponentProgressIfActive("系统优化", componentRenderVersion, msg)), force: true);
            }
            catch (Exception ex)
            {
                SetDownloadFailure("系统优化", "OptimizerNXT 组件", ex, AppPaths.OptimizerRuntime, "请在备用下载页下载 OptimizerNXT 组件压缩包，并将 zip 内容解压到下面的目标目录。");
                LogService.Write($"OptimizerNXT 组件获取失败：{ex}");
            }
            finally
            {
                _optimizerComponentBusy = false;
                await RefreshComponentPageIfActiveAsync("系统优化", componentRenderVersion);
            }
        }), false);
        AddActionButton("刷新", async () =>
        {
            _optimizer.InvalidateLiveStateCache();
            await SelectPageAsync("系统优化");
        });
        AddActionButton("扫描并恢复", async () => await RestoreAllAsync(), true);
        if (!AdminService.IsAdministrator) AddActionButton("管理员重启", () => AdminService.RestartAsAdministrator());

        AddOptimizerStatusCard(status);
        AddDownloadFailureCard("系统优化");
        var locked = status.State != ComponentState.Online;

        var items = await _optimizer.GetItemsAsync();
        if (!CanApplyPageUi("系统优化", renderVersion)) return;
        Dictionary<string, OptimizationStateInfo> liveStates;
        if (_optimizer.TryGetLiveStateCache(items, out liveStates, out var stale))
        {
            if (stale) _ = RefreshOptimizerStatesInBackgroundAsync(items, renderVersion, !locked);
        }
        else
        {
            var stateLoading = AddLoadingCard("正在核对 24 个优化项目的真实系统状态，首次扫描可能需要几秒...");
            liveStates = await _optimizer.GetLiveStatesAsync(items);
            if (!CanApplyPageUi("系统优化", renderVersion)) return;
            ContentPanel.Children.Remove(stateLoading);
        }
        if (!CanApplyPageUi("系统优化", renderVersion)) return;
        ContentPanel.Children.Add(CreateOptimizerGrid(items, liveStates, !locked));
    }

    private UniformGrid CreateOptimizerGrid(List<OptimizerItem> items, Dictionary<string, OptimizationStateInfo> liveStates, bool enabled)
    {
        var grid = CreateCardGrid();
        grid.Uid = "OptimizerCards";
        foreach (var item in items.OrderBy(OptimizerSortRisk).ThenBy(OptimizerSortRecommended).ThenBy(x => OptimizerGroupOrder(x.Group)).ThenBy(x => x.Order))
        {
            grid.Children.Add(CreateOptimizerCard(item, liveStates[item.Id], enabled));
        }
        return grid;
    }

    private async Task RefreshOptimizerStatesInBackgroundAsync(List<OptimizerItem> items, int renderVersion, bool enabled)
    {
        try
        {
            var liveStates = await _optimizer.GetLiveStatesAsync(items, forceRefresh: true);
            if (!CanApplyPageUi("系统优化", renderVersion)) return;
            var oldGrid = ContentPanel.Children.OfType<UniformGrid>().FirstOrDefault(x => x.Uid == "OptimizerCards");
            if (oldGrid == null) return;
            var index = ContentPanel.Children.IndexOf(oldGrid);
            ContentPanel.Children.RemoveAt(index);
            ContentPanel.Children.Insert(index, CreateOptimizerGrid(items, liveStates, enabled));
            StatusText.Text = $"状态已在后台更新 · {DateTime.Now:HH:mm}";
        }
        catch (Exception ex)
        {
            if (CanApplyPageUi("系统优化", renderVersion)) StatusText.Text = "后台状态更新失败，已保留上次结果";
            LogService.Write($"优化状态后台更新失败：{ex}");
        }
    }

    private void AddOptimizerStatusCard(ComponentStatus status)
    {
        var description = status.State switch
        {
            ComponentState.Online => "组件已就绪，可执行并回退系统优化项目。",
            ComponentState.Downloading => "正在获取 OptimizerNXT 组件，请稍候。",
            _ => "OptimizerNXT 组件未就绪时无法执行系统优化，请先获取远端服务。"
        };
        var meta = status.State == ComponentState.Online ? $"{status.ReadyCount} 项可用" : ComponentMetaText(status);
        AddComponentHeader("OptimizerNXT", OptimizerService.PinnedVersion, description, meta, ComponentMetaColor(status), status.State != ComponentState.Online);
    }

    private Border CreateOptimizerCard(OptimizerItem item, OptimizationStateInfo live, bool enabled)
    {
        var card = CardBorder();
        card.Tag = item.Id;
        MotionService.AttachCardMotion(card);
        card.Padding = new Thickness(16, 13, 14, 13);
        card.Height = 104;
        AttachHoverInfo(card, item.Description);
        var busy = _busyOptimizerIds.Contains(item.Id);
        _optimizerOperationText.TryGetValue(item.Id, out var operationText);
        operationText ??= "处理中";
        var applied = live.State is OptimizationLiveState.Optimized or OptimizationLiveState.OptimizedWithSkips;
        var canToggle = enabled && !busy && live.TotalTargets > 0 && live.State is OptimizationLiveState.Default or OptimizationLiveState.Optimized or OptimizationLiveState.OptimizedWithSkips;
        card.Cursor = busy ? Cursors.Wait : Cursors.Hand;
        card.Opacity = enabled ? 1 : 0.62;
        if (busy)
        {
            card.BorderBrush = BrushOf("Blue");
            card.Background = BrushOf("BlueSoft");
        }

        var root = new Grid { VerticalAlignment = VerticalAlignment.Center };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new DockPanel { LastChildFill = true };
        var badge = busy ? OperationBadge(operationText) : StateBadge(live.State, live.SkippedTargets);
        DockPanel.SetDock(badge, Dock.Right);
        titleRow.Children.Add(badge);
        titleRow.Children.Add(new TextBlock
        {
            Text = item.Title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = item.Risk == "high" ? BrushOf("Red") : BrushOf("Text"),
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 34,
            LineHeight = 18,
            VerticalAlignment = VerticalAlignment.Center
        });
        copy.Children.Add(titleRow);
        copy.Children.Add(new TextBlock
        {
            Text = busy ? $"{operationText}，完成后将自动读取并验证每个目标。" : live.State is OptimizationLiveState.Mixed or OptimizationLiveState.Unknown or OptimizationLiveState.NeedsReview or OptimizationLiveState.OptimizedWithSkips ? live.Detail : item.Description,
            Foreground = item.Risk == "high" ? BrushOf("Red") : BrushOf("Muted"),
            FontSize = 11,
            Margin = new Thickness(0, 4, 8, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Height = 18
        });
        UIElement state = busy ? CreateOptimizerOperationIndicator(operationText) : CreateOptimizerSwitch(item, live, canToggle);
        Grid.SetColumn(copy, 0);
        Grid.SetColumn(state, 1);
        root.Children.Add(copy);
        root.Children.Add(state);
        card.Child = root;
        if (!busy) card.MouseLeftButtonUp += (_, _) => ShowOptimizerStateDrawer(item, live);
        return card;
    }

    private Border OperationBadge(string text) => new()
    {
        Background = BrushOf("BlueSoft"), BorderBrush = BrushOf("Blue"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9),
        Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(10, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
        Child = new TextBlock { Text = text, Foreground = BrushOf("Blue"), FontSize = 10, FontWeight = FontWeights.SemiBold }
    };

    private StackPanel CreateOptimizerOperationIndicator(string text)
    {
        var panel = new StackPanel { Width = 64, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = text, Foreground = BrushOf("Blue"), FontSize = 10, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 4, Margin = new Thickness(0, 6, 0, 0) });
        return panel;
    }

    private Border StateBadge(OptimizationLiveState state, int skippedTargets = 0)
    {
        var (text, color) = state switch
        {
            OptimizationLiveState.Default => (skippedTargets > 0 ? $"未优化·跳过{skippedTargets}项" : "未优化", "Muted"),
            OptimizationLiveState.Optimized => ("已优化", "Green"),
            OptimizationLiveState.OptimizedWithSkips => ($"已优化·跳过{skippedTargets}项", "Green"),
            OptimizationLiveState.Mixed => ("混合", "Yellow"),
            OptimizationLiveState.NeedsReview => ("需要确认", "Yellow"),
            OptimizationLiveState.SafetyBlocked => ("安全阻止", "Yellow"),
            OptimizationLiveState.ExternallyManaged => ("策略管理", "Blue"),
            OptimizationLiveState.RestartRequired => ("需重启", "Yellow"),
            OptimizationLiveState.Unsupported => ("不适用", "Muted"),
            _ => ("待确认", "Yellow")
        };
        return new Border
        {
            Background = BrushOf("Panel3"), BorderBrush = BrushOf(color), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(10, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = text, Foreground = BrushOf(color), FontSize = 10, FontWeight = FontWeights.SemiBold }
        };
    }

    private static int OptimizerSortRisk(OptimizerItem item) => item.Risk == "high" ? 1 : 0;
    private static int OptimizerSortRecommended(OptimizerItem item) => item.EnabledByDefault || item.DefaultChecked ? 0 : 1;
    private static int OptimizerGroupOrder(string group) => group switch
    {
        "基础优化" => 10,
        "启动与后台" => 20,
        "性能增强" => 30,
        "浏览器与遥测" => 40,
        "应用遥测" => 50,
        "输入体验" => 60,
        "内存与预读取" => 70,
        "显卡与服务" => 80,
        "右键菜单" => 90,
        "系统更新" => 100,
        "安全相关" => 110,
        "存储与清理" => 120,
        "电源与硬件" => 130,
        _ => 999
    };

    private Border CreateOptimizerSwitch(OptimizerItem item, OptimizationStateInfo live, bool enabled)
    {
        var applied = live.State is OptimizationLiveState.Optimized or OptimizationLiveState.OptimizedWithSkips;
        var pill = new Border
        {
            Width = 54,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = applied ? BrushOf("Blue") : BrushOf("Panel3"),
            BorderBrush = applied ? BrushOf("Blue") : BrushOf("Border"),
            BorderThickness = new Thickness(1),
            Cursor = enabled ? Cursors.Hand : Cursors.Arrow,
            Margin = new Thickness(12, 0, 0, 0),
            Opacity = enabled ? 1 : 0.75,
            VerticalAlignment = VerticalAlignment.Center
        };
        var knobTransform = new TranslateTransform(applied ? 26 : 0, 0);
        var knob = new Ellipse
        {
            Width = 20,
            Height = 20,
            Fill = BrushOf("Text"),
            Margin = new Thickness(4, 3, 28, 3),
            RenderTransform = knobTransform
        };
        pill.Child = knob;
        if (enabled)
        {
            pill.MouseLeftButtonUp += async (_, e) =>
            {
                e.Handled = true;
                await ToggleOptimizerAsync(item, live);
            };
        }
        return pill;
    }

    private async Task ToggleOptimizerAsync(OptimizerItem item, OptimizationStateInfo live)
    {
        var applied = live.State is OptimizationLiveState.Optimized or OptimizationLiveState.OptimizedWithSkips;
        if (live.State is not (OptimizationLiveState.Default or OptimizationLiveState.Optimized or OptimizationLiveState.OptimizedWithSkips))
        {
            ShowOptimizerStateDrawer(item, live);
            return;
        }
        if (applied && live.Rollback != RollbackCapability.ExactSnapshot)
        {
            OpenDrawer("没有精确回退快照", item.Title,
                new TextBlock { Text = "实时检测表明该项目已优化，但本地没有对应的操作前快照。为避免覆盖用户或其他软件的原有设置，只能先生成安全默认恢复预检。", TextWrapping = TextWrapping.Wrap, LineHeight = 22 },
                "生成恢复预检", async () =>
                {
                    var plan = await _optimizer.BuildRestorePlanAsync(new[] { item.Id });
                    ShowRestorePlanDrawer(plan);
                });
            return;
        }
        var pendingSystemSkips = live.TargetDetails.Count(x => x.Applicability is TargetApplicability.NotApplicable or TargetApplicability.OptionalMissing or TargetApplicability.Unsafe or TargetApplicability.UnsupportedBuild);
        if (!applied && pendingSystemSkips > 0)
        {
            var skipped = new StackPanel();
            skipped.Children.Add(new TextBlock { Text = $"适用目标将继续执行；以下 {pendingSystemSkips} 项只会被排除，不会强制创建或修改。", TextWrapping = TextWrapping.Wrap, LineHeight = 21, Margin = new Thickness(0, 0, 0, 12) });
            AddOptimizationTargetGroup(skipped, "本机不适用／安全跳过", live.TargetDetails.Where(x => x.Applicability is TargetApplicability.NotApplicable or TargetApplicability.OptionalMissing or TargetApplicability.Unsafe or TargetApplicability.UnsupportedBuild), "Yellow");
            OpenDrawer("确认跳过后继续", item.Title, skipped, $"确认并忽略 {pendingSystemSkips} 项", async () => await ExecuteOptimizerToggleAsync(item, false, true));
            return;
        }
        if (!applied && item.Risk == "high")
        {
            OpenDrawer("高风险优化确认", item.Title, new TextBlock { Text = item.Description + "\n\n该操作可能改变 Windows 安全、更新或硬件兼容行为。执行前会创建精确快照。", TextWrapping = TextWrapping.Wrap, LineHeight = 22 }, "仍要执行", async () => await ExecuteOptimizerToggleAsync(item, false, false));
            return;
        }
        await ExecuteOptimizerToggleAsync(item, applied, false);
    }

    private async Task ExecuteOptimizerToggleAsync(OptimizerItem item, bool applied, bool confirmSkips)
    {
        await CloseDrawerAsync();
        if (!_busyOptimizerIds.Add(item.Id)) return;
        _optimizerOperationText[item.Id] = applied ? "回退中" : "优化中";
        var operationRenderVersion = _renderVersion;
        try
        {
            await SelectPageAsync("系统优化");
            operationRenderVersion = _renderVersion;
            await RunGuardedAsync(async () =>
            {
                EnsureAdminOrThrow();
                if (applied)
                {
                    UpdateComponentProgressIfActive("系统优化", operationRenderVersion, $"正在回退：{item.Title}");
                    var report = await _optimizer.RestoreAsync(item.Id);
                    if (!CanApplyPageUi("系统优化", operationRenderVersion)) return;
                    ShowRollbackResultsDrawer(report, item);
                    StatusText.Text = report.Ok ? $"已验证回退：{item.Title}" : $"回退未完成：{item.Title}";
                }
                else
                {
                    UpdateComponentProgressIfActive("系统优化", operationRenderVersion, $"正在优化：{item.Title}");
                    await _optimizer.ApplyAsync(item, _shutdown.Token, confirmSkips);
                    UpdateComponentProgressIfActive("系统优化", operationRenderVersion, $"已优化：{item.Title}");
                }
            });
        }
        finally
        {
            _busyOptimizerIds.Remove(item.Id);
            _optimizerOperationText.Remove(item.Id);
            await RefreshComponentPageIfActiveAsync("系统优化", operationRenderVersion);
        }
    }

    private async Task RenderCleanerAsync(int renderVersion)
    {
        var status = await _cleaner.GetStatusAsync();
        if (!CanApplyPageUi("系统清理", renderVersion)) return;
        if (_cleanerComponentBusy) status = status with { State = ComponentState.Downloading, Text = "获取远端服务中" };
        AddActionButton(status.State == ComponentState.Online ? "重新获取组件" : "获取清理组件", async () => await RunGuardedAsync(async () =>
        {
            ClearDownloadFailure("系统清理");
            _cleanerComponentBusy = true;
            await SelectPageAsync("系统清理");
            var componentRenderVersion = _renderVersion;
            try
            {
                await _cleaner.InstallAsync(_shutdown.Token, msg => Dispatcher.Invoke(() => UpdateComponentProgressIfActive("系统清理", componentRenderVersion, msg)), force: true);
            }
            catch (Exception ex)
            {
                SetDownloadFailure("系统清理", "BleachBit 组件", ex, AppPaths.CleanerRuntime, "请在备用下载页下载 BleachBit portable 压缩包，并将 zip 内容解压到下面的目标目录。");
                LogService.Write($"BleachBit 组件获取失败：{ex}");
            }
            finally
            {
                _cleanerComponentBusy = false;
                await RefreshComponentPageIfActiveAsync("系统清理", componentRenderVersion);
            }
        }));
        AddActionButton("恢复默认", async () => await ResetCleanerDefaultsAsync());
        AddActionButton("预览清理", async () => await PreviewSelectedCleanerAsync());
        AddActionButton("执行清理", async () => await CleanSelectedCleanerAsync());
        if (!AdminService.IsAdministrator) AddActionButton("管理员重启", () => AdminService.RestartAsAdministrator());

        if (status.State != ComponentState.Online)
        {
            AddCleanerHeader(status, new List<CleanerItem>());
            AddDownloadFailureCard("系统清理");
            return;
        }

        var items = await _cleaner.GetItemsAsync();
        if (!CanApplyPageUi("系统清理", renderVersion)) return;
        SyncCleanerSelection(items);
        AddCleanerHeader(status, items);
        AddDownloadFailureCard("系统清理");
        if (_lastCleanerSummary != null) AddCleanerSummaryCard(_lastCleanerSummary);

        foreach (var group in items.GroupBy(x => x.Group).OrderBy(x => x.Key))
        {
            var expander = new Expander
            {
                Header = $"{group.Key} · {group.Count(x => _selectedCleanerIds.Contains(x.Id))} 项已选择 / {group.Count()} 项",
                IsExpanded = group.Key is "Windows" or "浏览器",
                Foreground = BrushOf("Text"),
                Background = Brushes.Transparent,
                Margin = new Thickness(0, 0, 0, 12)
            };
            var panel = new StackPanel();
            foreach (var item in group) panel.Children.Add(CreateCleanerRow(item));
            expander.Content = panel;
            ContentPanel.Children.Add(expander);
        }
    }

    private void AddCleanerHeader(ComponentStatus status, List<CleanerItem> items)
    {
        var description = status.State switch
        {
            ComponentState.Online => "清理组件已就绪，默认只选择适合定期清理的安全项目。",
            ComponentState.Downloading => "正在获取 BleachBit 组件，请稍候。",
            _ => "BleachBit 组件未就绪时无法预览或执行清理，请先获取清理组件。"
        };
        var meta = status.State == ComponentState.Online ? $"{items.Count(x => x.Applicable)} 项适用 / {items.Count} 项定义" : ComponentMetaText(status);
        AddComponentHeader("BleachBit", CleanerService.PinnedVersion, description, meta, ComponentMetaColor(status), status.State != ComponentState.Online);
    }

    private void SyncCleanerSelection(List<CleanerItem> items)
    {
        var available = items.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _selectedCleanerIds.RemoveWhere(x => !available.Contains(x));
        if (!_cleanerSelectionInitialized)
        {
            foreach (var item in items.Where(x => x.DefaultSelected))
                _selectedCleanerIds.Add(item.Id);
            _cleanerSelectionInitialized = true;
        }
    }

    private Border CreateCleanerRow(CleanerItem item)
    {
        var card = CardBorder();
        card.Margin = new Thickness(0, 0, 0, 10);
        card.Opacity = item.Applicable ? 1 : 0.62;
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var copy = new StackPanel();
        copy.Children.Add(new TextBlock
        {
            Text = item.Title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = item.Risk == "high" ? BrushOf("Red") : BrushOf("Text")
        });
        copy.Children.Add(new TextBlock { Text = item.Description + $"\n{item.SelectionReason}" + (string.IsNullOrWhiteSpace(item.Warning) ? "" : $" · 注意：{item.Warning}"), Foreground = BrushOf("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 12, 0), LineHeight = 18 });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(CreateCleanerSwitch(item));
        Grid.SetColumn(copy, 0);
        Grid.SetColumn(buttons, 1);
        root.Children.Add(copy);
        root.Children.Add(buttons);
        card.Child = root;
        return card;
    }

    private Border CreateCleanerSwitch(CleanerItem item)
    {
        var selected = _selectedCleanerIds.Contains(item.Id);
        var pill = new Border
        {
            Width = 62,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = selected ? BrushOf("Blue") : BrushOf("Panel3"),
            BorderBrush = selected ? BrushOf("Blue") : BrushOf("Border"),
            BorderThickness = new Thickness(1),
            Cursor = item.Applicable ? Cursors.Hand : Cursors.Arrow,
            IsEnabled = item.Applicable,
            Margin = new Thickness(10, 0, 0, 0)
        };
        var dot = new Ellipse
        {
            Width = 22,
            Height = 22,
            Fill = BrushOf("Text"),
            Margin = selected ? new Thickness(33, 4, 4, 4) : new Thickness(5, 4, 33, 4)
        };
        pill.Child = dot;
        pill.MouseLeftButtonUp += async (_, _) =>
        {
            if (!item.Applicable) return;
            if (selected) _selectedCleanerIds.Remove(item.Id);
            else _selectedCleanerIds.Add(item.Id);
            _lastCleanerPreviewIds = null;
            await SelectPageAsync("系统清理");
        };
        return pill;
    }

    private async Task ResetCleanerDefaultsAsync()
    {
        var renderVersion = _renderVersion;
        var items = await _cleaner.GetItemsAsync();
        if (!CanApplyPageUi("系统清理", renderVersion)) return;
        _selectedCleanerIds.Clear();
        foreach (var item in items.Where(x => x.DefaultSelected))
            _selectedCleanerIds.Add(item.Id);
        _cleanerSelectionInitialized = true;
        _lastCleanerPreviewIds = null;
        _lastCleanerSummary = null;
        await SelectPageAsync("系统清理");
    }

    private async Task PreviewSelectedCleanerAsync()
    {
        var renderVersion = _renderVersion;
        await RunGuardedAsync(async () =>
        {
            var ids = _selectedCleanerIds.ToList();
            if (ids.Count == 0) throw new InvalidOperationException("请至少选择一个清理项目。");
            _cleanerRunBusy = true;
            _lastCleanerSummary = CleanerLoadingSummary("--preview", ids.Count);
            await SelectPageAsync("系统清理");
            renderVersion = _renderVersion;
            try
            {
                var result = await _cleaner.RunAsync("--preview", ids, _shutdown.Token);
                _lastCleanerSummary = result.Summary;
                _lastCleanerPreviewIds = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            finally
            {
                _cleanerRunBusy = false;
                await RefreshComponentPageIfActiveAsync("系统清理", renderVersion);
            }
        });
    }

    private async Task CleanSelectedCleanerAsync()
    {
        var renderVersion = _renderVersion;
        await RunGuardedAsync(async () =>
        {
            EnsureAdminOrThrow();
            var items = await _cleaner.GetItemsAsync();
            if (!CanApplyPageUi("系统清理", renderVersion)) return;
            var selected = items.Where(x => _selectedCleanerIds.Contains(x.Id)).ToList();
            if (selected.Count == 0) throw new InvalidOperationException("请至少选择一个清理项目。");
            if (_lastCleanerPreviewIds == null || !_lastCleanerPreviewIds.SetEquals(selected.Select(x => x.Id)))
                throw new InvalidOperationException("清理项目已经变化或尚未预览。请先点击“预览清理”，确认范围和预计释放空间后再执行。");
            var highRisk = selected.Where(x => x.Risk == "high").ToList();
            if (highRisk.Count > 0 && MessageBox.Show($"已选择 {highRisk.Count} 个高风险清理项，可能清除登录状态、历史记录或敏感缓存。确认继续吗？", "高风险确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _cleanerRunBusy = true;
            _lastCleanerSummary = CleanerLoadingSummary("--clean", selected.Count);
            await SelectPageAsync("系统清理");
            renderVersion = _renderVersion;
            UpdateComponentProgressIfActive("系统清理", renderVersion, $"正在清理 {selected.Count} 项...");
            try
            {
                var result = await _cleaner.RunAsync("--clean", selected.Select(x => x.Id), _shutdown.Token);
                _lastCleanerSummary = result.Summary;
            }
            finally
            {
                _cleanerRunBusy = false;
                await RefreshComponentPageIfActiveAsync("系统清理", renderVersion);
            }
        });
    }

    private static CleanerRunSummary CleanerLoadingSummary(string mode, int selectedItems)
    {
        var preview = mode.Contains("preview", StringComparison.OrdinalIgnoreCase);
        return new CleanerRunSummary(mode, selectedItems, 0, 0, preview ? "正在计算" : "正在清理", preview ? "正在分析预计清理内容..." : "正在执行清理任务...", true, new List<CleanerPathInfo>());
    }

    private void AddCleanerSummaryCard(CleanerRunSummary summary)
    {
        var preview = summary.Mode.Contains("preview", StringComparison.OrdinalIgnoreCase);
        var card = CardBorder();
        card.BorderBrush = BrushOf(preview ? "Blue" : "Green");
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = preview ? "清理预设" : "清理结果",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushOf(preview ? "Blue" : "Green")
        });

        var row = new UniformGrid { Columns = 3, Margin = new Thickness(0, 12, 0, 10) };
        row.Children.Add(MetricBlock(preview ? "预计清理项目" : "已清理项目", $"{summary.SelectedItems} 项"));
        row.Children.Add(MetricBlock(preview ? "预计文件数量" : "处理文件数量", summary.FileCount > 0 ? $"{summary.FileCount} 个" : "未识别"));
        row.Children.Add(MetricBlock(preview ? "预计释放空间" : "释放磁盘空间", summary.SpaceText));
        panel.Children.Add(row);
        if (summary.IsLoading || _cleanerRunBusy)
        {
            panel.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 6, Margin = new Thickness(0, 4, 0, 10) });
            panel.Children.Add(new TextBlock { Text = summary.OutputPreview, Foreground = BrushOf("Muted"), FontSize = 13 });
        }
        else
        {
            AddCleanerPathPreview(panel, summary);
        }
        card.Child = panel;
        ContentPanel.Children.Add(card);
    }

    private void AddCleanerPathPreview(StackPanel panel, CleanerRunSummary summary)
    {
        if (summary.TopPaths.Count == 0)
            return;

        foreach (var item in summary.TopPaths.Take(5))
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock
            {
                Text = item.Path,
                Foreground = BrushOf("Muted"),
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var size = new TextBlock
            {
                Text = item.SizeText,
                Foreground = BrushOf("Text"),
                FontSize = 12,
                Margin = new Thickness(12, 0, 0, 0)
            };
            Grid.SetColumn(size, 1);
            row.Children.Add(size);
            panel.Children.Add(row);
        }
    }

    private UIElement MetricBlock(string label, string value)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        panel.Children.Add(new TextBlock { Text = label, Foreground = BrushOf("Muted"), FontSize = 12 });
        panel.Children.Add(new TextBlock { Text = value, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 0, 0) });
        return panel;
    }

    private async Task RenderDriversAsync(int renderVersion)
    {
        AddActionButton("刷新", async () =>
        {
            _forceDriverRefresh = true;
            await SelectPageAsync("驱动管理");
        });
        var hasCached = _drivers.TryGetPageCache(out var cached, out var stale) && cached != null;
        if (!_forceDriverRefresh && hasCached)
        {
            RenderDriverData(cached!);
            if (stale) _ = RefreshDriversInBackgroundAsync(renderVersion);
            return;
        }
        if (_forceDriverRefresh && hasCached) RenderDriverData(cached!);
        var loading = AddLoadingCard(_forceDriverRefresh && hasCached ? "正在后台刷新驱动状态，当前仍显示上次结果..." : _forceDriverRefresh ? "正在刷新驱动状态..." : "正在读取驱动状态...");
        try
        {
            var data = await _drivers.GetPageDataAsync(_forceDriverRefresh, _shutdown.Token);
            _forceDriverRefresh = false;
            if (renderVersion != _renderVersion || _active != "驱动管理") return;
            ContentPanel.Children.Clear();
            RenderDriverData(data);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _forceDriverRefresh = false;
            if (renderVersion != _renderVersion || _active != "驱动管理") return;
            ContentPanel.Children.Remove(loading);
            AddInfoCard(hasCached ? "刷新失败，已保留上次结果" : "驱动状态加载失败", ex.Message, true);
            LogService.Write($"驱动状态加载失败：{ex}");
        }
    }

    private async Task RefreshDriversInBackgroundAsync(int renderVersion)
    {
        try
        {
            var data = await _drivers.GetPageDataAsync(forceRefresh: true, _shutdown.Token);
            if (renderVersion == _renderVersion && _active == "驱动管理")
            {
                ContentPanel.Children.Clear();
                RenderDriverData(data);
                StatusText.Text = $"驱动信息已在后台更新 · {DateTime.Now:HH:mm}";
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (renderVersion == _renderVersion && _active == "驱动管理") StatusText.Text = "后台刷新失败，已保留上次驱动信息";
            LogService.Write($"驱动信息后台刷新失败：{ex}");
        }
    }

    private void RenderDriverData(DriverPageData data)
    {
        AddDriverHealthCard(data);
        var info = data.Device;
        var identity = data.Identity;
        var vendorText = string.IsNullOrWhiteSpace(info.VendorDisplayName) ? "暂无信息" : info.VendorDisplayName;
        var confidenceSuffix = identity.Confidence == IdentityConfidence.High ? "" : $"  ·  {IdentityConfidenceText(identity.Confidence)}";
        var deviceCard = ActionCard(
            $"当前设备{confidenceSuffix}",
            $"{info.Manufacturer} · {info.Model}\n厂商：{vendorText}\n固件报告 {info.DeviceIdentifierLabel}：{info.DeviceIdentifierValue}\n身份摘要：{identity.StableDigest}\n驱动状态缓存：{data.CachedAt:yyyy-MM-dd HH:mm:ss}",
            identity.Confidence == IdentityConfidence.Low ? "Yellow" : identity.Confidence == IdentityConfidence.High ? "Green" : "Border");
        var deviceActions = (StackPanel)((Grid)deviceCard.Child).Children[1];
        deviceActions.Children.Add(SmallButton("识别依据", () => ShowDriverIdentityDrawer(info, identity), 92));
        var copyIdentifier = SmallButton("复制", () =>
        {
            if (!info.CanCopyIdentifier) return;
            Clipboard.SetText(info.DeviceIdentifierValue);
            StatusText.Text = $"已复制 {info.DeviceIdentifierLabel}";
        }, 72);
        copyIdentifier.IsEnabled = info.CanCopyIdentifier;
        deviceActions.Children.Add(copyIdentifier);
        ContentPanel.Children.Add(deviceCard);
        if (identity.Confidence == IdentityConfidence.Low)
            AddInfoCard("机器识别置信度较低", "固件字段存在冲突、占位值或虚拟环境特征。软件不会据此自动匹配具体驱动包，请在厂商官网手动确认型号。", true, "Yellow");
        var actionCard = ActionCard("官方驱动中心", string.IsNullOrWhiteSpace(info.SupportPage) ? "未匹配到厂商官网入口。" : info.SupportPage, info.MatchSuccess ? "Green" : "Yellow");
        var actions = (StackPanel)((Grid)actionCard.Child).Children[1];
        if (!string.IsNullOrWhiteSpace(info.DownloadUrl))
            actions.Children.Add(SmallButton("一键下载", async () => await RunGuardedAsync(async () =>
            {
                StatusText.Text = "正在下载官方驱动包...";
                var path = await _drivers.DownloadDriverPackageAsync(info, _shutdown.Token);
                OpenDrawer("驱动包校验完成", "下载文件已通过域名、SHA-256 与数字签名校验。", new TextBlock { Text = path, TextWrapping = TextWrapping.Wrap }, "关闭", null);
            })));
        actions.Children.Add(SmallButton("打开官网", () =>
        {
            var url = string.IsNullOrWhiteSpace(info.SupportPage) ? "https://www.microsoft.com/windows" : info.SupportPage;
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }, 104));
        ContentPanel.Children.Add(actionCard);

        AddSectionTitle("驱动反馈", "主要设备");
        var grid = CreateCardGrid();
        foreach (var driver in data.Drivers)
            grid.Children.Add(SimpleMiniCard(driver.Name, driver.Detail, driver.Status));
        ContentPanel.Children.Add(grid);
    }

    private static string IdentityConfidenceText(IdentityConfidence confidence) => confidence switch
    {
        IdentityConfidence.High => "识别结果稳定",
        IdentityConfidence.Medium => "中等置信度",
        _ => "低置信度"
    };

    private void ShowDriverIdentityDrawer(DeviceMatchInfo info, DeviceIdentityAssessment identity)
    {
        var content = new StackPanel();
        content.Children.Add(new Border
        {
            Background = BrushOf(identity.Confidence == IdentityConfidence.Low ? "Panel3" : "BlueSoft"), CornerRadius = new CornerRadius(14), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 14),
            Child = new TextBlock { Text = identity.Confidence == IdentityConfidence.High ? identity.Summary : $"{IdentityConfidenceText(identity.Confidence)}\n{identity.Summary}", TextWrapping = TextWrapping.Wrap, LineHeight = 22 }
        });
        foreach (var candidate in identity.Candidates)
        {
            var row = new Border { Background = BrushOf("Panel2"), BorderBrush = BrushOf("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 9) };
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = candidate.Label, FontWeight = FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = candidate.Valid ? candidate.Value : "未能读取", FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 5, 0, 0) });
            panel.Children.Add(new TextBlock { Text = $"{candidate.Source} · {candidate.Note}", Foreground = BrushOf("Muted"), FontSize = 11, Margin = new Thickness(0, 5, 0, 0) });
            row.Child = panel;
            content.Children.Add(row);
        }
        if (identity.Conflicts.Count > 0)
            content.Children.Add(new TextBlock { Text = "冲突：\n" + string.Join("\n", identity.Conflicts.Select(x => "• " + x)), Foreground = BrushOf("Yellow"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), LineHeight = 21 });
        OpenDrawer("机器识别依据", "这些值均为 Windows 当前读取到的固件报告值，不能证明是修改前的原始出厂编号。", content, "关闭", null);
    }

    private Border AddLoadingCard(string text)
    {
        var card = CardBorder();
        card.Child = new TextBlock { Text = text, Foreground = BrushOf("Muted"), FontSize = 15 };
        ContentPanel.Children.Add(card);
        return card;
    }

    private void AddDriverHealthCard(DriverPageData data)
    {
        var abnormal = data.Drivers.Where(x => !x.Status.Contains("正常", StringComparison.OrdinalIgnoreCase)).ToList();
        var title = abnormal.Count == 0 ? "驱动状态正常" : $"发现 {abnormal.Count} 个驱动异常";
        var detail = abnormal.Count == 0
            ? $"已检查 {data.Drivers.Count} 个主要设备，未发现明显异常。"
            : string.Join("\n", abnormal.Take(4).Select(x => $"{x.Name}：{x.Status}"));
        AddInfoCard(title, detail, abnormal.Count > 0, abnormal.Count == 0 ? "Green" : "Yellow");
    }

    private async Task RenderToolsAsync()
    {
        AddAdvancedSystemTools();
        AddSectionTitle("工具箱", "固定 GitHub Release tools-v1");
        AddDownloadFailureCard("工具箱");
        var grid = CreateCardGrid();
        foreach (var tool in await _toolbox.GetToolsAsync())
        {
            var card = CreateToolCard(tool);
            var actions = (StackPanel)((Grid)card.Child).Children[1];
            actions.Children.Add(SmallButton("启动", async () => await RunGuardedAsync(async () =>
            {
                if (tool.Dangerous)
                {
                    var warning = tool.Id == "disableSecurityUpdate"
                        ? "该独立工具既能修改 Windows Update，也可能关闭 Defender 安全中心、更新服务和 Microsoft Store 依赖组件。它不属于 24 项优化、不使用 YAML，且不在本软件的全局恢复承诺内。"
                        : tool.Id == "smartDns"
                            ? "该工具会修改当前活动 IPv4 网络适配器的 DNS 设置，不属于本软件的全局恢复范围。需要恢复时，请在工具内选择“恢复 DHCP/路由器自动 DNS”。"
                        : "该工具由独立程序执行，可能修改系统设置，不属于本软件的全局恢复范围。";
                    OpenDrawer("高级工具确认", tool.Name, new TextBlock { Text = tool.Description + "\n\n" + warning, TextWrapping = TextWrapping.Wrap, LineHeight = 22 }, "确认启动", async () => await LaunchToolAsync(tool));
                    return;
                }
                await LaunchToolAsync(tool);
            })));
            grid.Children.Add(card);
        }
        ContentPanel.Children.Add(grid);
    }

    private async Task LaunchToolAsync(ToolItem tool)
    {
        await CloseDrawerAsync();
        ClearDownloadFailure("工具箱");
        StatusText.Text = $"正在准备工具：{tool.Name}";
        try { StatusText.Text = await _toolbox.DownloadAndLaunchAsync(tool, _shutdown.Token); }
        catch (Exception ex)
        {
            SetDownloadFailure("工具箱", tool.Name, ex, AppPaths.ToolRuntime, "请在备用下载页下载对应工具压缩包，并将 zip 内容解压到下面的目标目录。");
            throw;
        }
        finally { if (_active == "工具箱") await SelectPageAsync("工具箱"); }
    }

    private void AddAdvancedSystemTools()
    {
        AddSectionTitle("高级系统操作", "不参与全局优化恢复");
        var restore = ActionCard("系统还原管理  ·  高级", "单独管理系统保护。停用操作不会删除已有还原点；已有还原点一旦被其他工具删除则无法找回。", "Yellow");
        var restoreActions = (StackPanel)((Grid)restore.Child).Children[1];
        restoreActions.Children.Add(SmallButton("打开设置", () => Process.Start(new ProcessStartInfo("SystemPropertiesProtection.exe") { UseShellExecute = true }), 88));
        restoreActions.Children.Add(SmallButton("恢复保护", async () => await RunGuardedAsync(async () => await SetSystemRestoreAsync(true)), 88));
        ContentPanel.Children.Add(restore);

        var oneDrive = ActionCard("OneDrive 管理  ·  高级", "卸载与重新安装从普通优化中移出。卸载不会删除云端文件，但可能移除本机同步状态。", "Yellow");
        var oneDriveActions = (StackPanel)((Grid)oneDrive.Child).Children[1];
        oneDriveActions.Children.Add(SmallButton("重新安装", async () => await RunGuardedAsync(async () => await RunOneDriveSetupAsync(false)), 88));
        var uninstall = SmallButton("卸载", () => OpenDrawer("卸载 OneDrive", "这是独立的高级操作，不属于可逆优化。", new TextBlock { Text = "继续后将调用 Windows 自带 OneDrive 安装程序执行卸载。本机同步配置可能需要重新设置。", TextWrapping = TextWrapping.Wrap, LineHeight = 22 }, "确认卸载", async () => await RunOneDriveSetupAsync(true)), 72);
        uninstall.Style = (Style)Application.Current.Resources["DangerButtonStyle"];
        oneDriveActions.Children.Add(uninstall);
        ContentPanel.Children.Add(oneDrive);
    }

    private async Task SetSystemRestoreAsync(bool enable)
    {
        EnsureAdminOrThrow();
        var command = enable ? "Enable-ComputerRestore -Drive 'C:\\'; Checkpoint-Computer -Description 'WindowsLite 安全恢复点' -RestorePointType MODIFY_SETTINGS -ErrorAction SilentlyContinue" : "Disable-ComputerRestore -Drive 'C:\\'";
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command));
        var result = await RunLocalProcessAsync("powershell.exe", $"-NoProfile -EncodedCommand {encoded}");
        if (result.ExitCode != 0) throw new InvalidOperationException(result.Output);
        StatusText.Text = enable ? "系统保护已恢复，并尝试创建安全恢复点" : "系统保护已停用（未删除已有还原点）";
    }

    private async Task RunOneDriveSetupAsync(bool uninstall)
    {
        EnsureAdminOrThrow();
        await CloseDrawerAsync();
        var candidates = new[] { System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64", "OneDriveSetup.exe"), System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "OneDriveSetup.exe") };
        var setup = candidates.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("Windows 自带 OneDriveSetup.exe 不存在，请从 Microsoft 官网重新获取 OneDrive。");
        var result = await RunLocalProcessAsync(setup, uninstall ? "/uninstall" : "");
        if (result.ExitCode != 0) throw new InvalidOperationException(result.Output);
        StatusText.Text = uninstall ? "OneDrive 卸载流程已完成" : "OneDrive 安装程序已完成";
    }

    private static async Task<(int ExitCode, string Output)> RunLocalProcessAsync(string file, string arguments)
    {
        var psi = new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动系统操作。");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, ((await stdout) + Environment.NewLine + (await stderr)).Trim());
    }

    private Border CreateToolCard(ToolItem tool)
    {
        var card = CardBorder();
        card.Height = 112;
        AttachHoverInfo(card, tool.Description);

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock
        {
            Text = tool.Name,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 38
        });
        copy.Children.Add(new TextBlock
        {
            Text = tool.Description,
            Foreground = BrushOf("Muted"),
            FontSize = 12,
            Margin = new Thickness(0, 5, 12, 0),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 34
        });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(copy, 0);
        Grid.SetColumn(actions, 1);
        root.Children.Add(copy);
        root.Children.Add(actions);
        card.Child = root;
        return card;
    }

    private void RenderSettings()
    {
        AddSectionTitle("主题设置", "");
        var themeCard = CardBorder();
        var themePanel = new StackPanel();
        themePanel.Children.Add(new TextBlock { Text = "界面主题", FontSize = 17, FontWeight = FontWeights.SemiBold });
        var combo = new ComboBox { Margin = new Thickness(0, 12, 0, 0), Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
        combo.Items.Add("Dark");
        combo.Items.Add("Light");
        combo.Items.Add("System");
        combo.SelectedItem = _settings.ThemeMode;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string value)
            {
                _settings.ThemeMode = value;
                SettingsService.Save(_settings);
                ApplyTheme();
                _ = SelectPageAsync("设置");
            }
        };
        themePanel.Children.Add(combo);
        themeCard.Child = themePanel;
        ContentPanel.Children.Add(themeCard);

        AddSectionTitle("本地组件", "");
        var componentCard = CardBorder();
        var componentPanel = new StackPanel();
        var keep = new CheckBox { Content = "关闭软件后保留本地组件", IsChecked = _settings.KeepLocalComponents, FontSize = 16, Margin = new Thickness(0, 0, 0, 12) };
        keep.Checked += (_, _) => { _settings.KeepLocalComponents = true; SettingsService.Save(_settings); };
        keep.Unchecked += (_, _) => { _settings.KeepLocalComponents = false; SettingsService.Save(_settings); };
        componentPanel.Children.Add(keep);
        componentPanel.Children.Add(new TextBlock { Text = AppPaths.Runtime, Foreground = BrushOf("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
        componentPanel.Children.Add(SmallButton("打开本地组件位置", () =>
        {
            Directory.CreateDirectory(AppPaths.Runtime);
            Process.Start(new ProcessStartInfo(AppPaths.Runtime) { UseShellExecute = true });
        }));
        componentCard.Child = componentPanel;
        ContentPanel.Children.Add(componentCard);
    }

    private Border ActionCard(string title, string description, string colorKey)
    {
        var card = CardBorder();
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var copy = new StackPanel();
        copy.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold });
        copy.Children.Add(new TextBlock { Text = description, Foreground = BrushOf("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 16, 0), MaxWidth = 650 });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(copy, 0);
        Grid.SetColumn(actions, 1);
        root.Children.Add(copy);
        root.Children.Add(actions);
        card.Child = root;
        return card;
    }

    private Border SimpleMiniCard(string title, string description, string status)
    {
        var card = CardBorder();
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = description, Foreground = BrushOf("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
        panel.Children.Add(new TextBlock { Text = status, Foreground = BrushOf("Green"), FontSize = 12, Margin = new Thickness(0, 6, 0, 0) });
        card.Child = panel;
        return card;
    }

    private void AddInfoCard(string title, string description, bool warning, string? colorKey = null)
    {
        var card = CardBorder();
        card.BorderBrush = BrushOf(colorKey ?? (warning ? "Yellow" : "Border"));
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = warning ? BrushOf("Yellow") : BrushOf("Text") });
        panel.Children.Add(new TextBlock { Text = description, Foreground = BrushOf("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        card.Child = panel;
        ContentPanel.Children.Add(card);
    }

    private void AddDownloadFailureCard(string page)
    {
        if (_lastDownloadFailure == null || _lastDownloadFailure.Page != page) return;
        var failure = _lastDownloadFailure;
        var card = CardBorder();
        card.BorderBrush = BrushOf("Yellow");
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var copy = new StackPanel();
        copy.Children.Add(new TextBlock
        {
            Text = "备用下载方法",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushOf("Yellow")
        });
        copy.Children.Add(new TextBlock
        {
            Text = $"{failure.Module} 自动下载未完成：{failure.Reason}",
            Foreground = BrushOf("Text"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 18, 0)
        });
        copy.Children.Add(new TextBlock
        {
            Text = $"{failure.Instruction}\n目标目录：{failure.TargetDirectory}\n完成后回到软件重新点击获取组件，或重新打开该页面检测状态。",
            Foreground = BrushOf("Muted"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 18, 0),
            LineHeight = 23
        });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(SmallButton("打开备用下载页", () =>
        {
            Process.Start(new ProcessStartInfo(ManualDownloadUrl) { UseShellExecute = true });
        }, 142));
        Grid.SetColumn(copy, 0);
        Grid.SetColumn(actions, 1);
        root.Children.Add(copy);
        root.Children.Add(actions);
        card.Child = root;
        ContentPanel.Children.Add(card);
    }

    private void SetDownloadFailure(string page, string module, Exception ex, string targetDirectory, string instruction)
    {
        _lastDownloadFailure = new DownloadFailure(page, module, DownloadFailureReason(ex), targetDirectory, instruction);
    }

    private void ClearDownloadFailure(string page)
    {
        if (_lastDownloadFailure?.Page == page) _lastDownloadFailure = null;
    }

    private static string DownloadFailureReason(Exception ex)
    {
        var text = ex.ToString();
        if (text.Contains("429") || text.Contains("TooManyRequests", StringComparison.OrdinalIgnoreCase) || text.Contains("请求过于频繁"))
            return "远端请求过于频繁或被限流。";
        if (text.Contains("SHA", StringComparison.OrdinalIgnoreCase) || text.Contains("校验失败"))
            return "文件校验失败，已阻止使用该下载结果。";
        if (text.Contains("timed out", StringComparison.OrdinalIgnoreCase) || text.Contains("timeout", StringComparison.OrdinalIgnoreCase) || text.Contains("超时") || text.Contains("速度过慢"))
            return "下载超时或网络速度过慢。";
        if (text.Contains("NameResolution", StringComparison.OrdinalIgnoreCase) || text.Contains("No such host", StringComparison.OrdinalIgnoreCase) || text.Contains("无法解析"))
            return "无法连接远端服务器，可能是 DNS 或网络连接异常。";
        if (text.Contains("GitHub", StringComparison.OrdinalIgnoreCase) || text.Contains("github.com", StringComparison.OrdinalIgnoreCase))
            return "无法稳定连接 GitHub 下载源。";
        if (ex is HttpRequestException)
            return "远端下载请求失败。";
        if (ex is UnauthorizedAccessException || text.Contains("占用") || text.Contains("权限"))
            return "本地文件被占用或没有写入权限。";
        return ex.Message;
    }

    private void AddComponentHeader(string name, string version, string description, string meta, string metaColorKey, bool warning)
    {
        var card = CardBorder();
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = BrushOf("Text"),
            VerticalAlignment = VerticalAlignment.Center
        });
        title.Children.Add(new TextBlock
        {
            Text = version,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushOf("Muted"),
            Margin = new Thickness(10, 2, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        copy.Children.Add(title);
        copy.Children.Add(new TextBlock
        {
            Text = description,
            Foreground = BrushOf("Muted"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 18, 0),
            MaxWidth = 720
        });

        var status = new TextBlock
        {
            Text = meta,
            Foreground = BrushOf(metaColorKey),
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(copy, 0);
        Grid.SetColumn(status, 1);
        root.Children.Add(copy);
        root.Children.Add(status);
        card.Child = root;
        ContentPanel.Children.Add(card);
    }

    private static string ComponentMetaText(ComponentStatus status) => status.State switch
    {
        ComponentState.Online => "在线",
        ComponentState.Downloading => "获取中",
        _ => "离线"
    };

    private static string ComponentMetaColor(ComponentStatus status) => status.State switch
    {
        ComponentState.Online => "Green",
        ComponentState.Downloading => "Yellow",
        _ => "Red"
    };

    private void AddSectionTitle(string title, string meta)
    {
        var row = new DockPanel { Margin = new Thickness(2, 12, 2, 8) };
        row.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold });
        if (!string.IsNullOrWhiteSpace(meta))
        {
            var right = new TextBlock { Text = meta, Foreground = BrushOf("Muted"), HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
        }
        ContentPanel.Children.Add(row);
    }

    private UniformGrid CreateCardGrid()
    {
        var grid = new UniformGrid { Columns = MainContentGrid.ActualWidth >= 760 ? 2 : 1, Margin = new Thickness(0, 0, 0, 6), Tag = "ResponsiveCards" };
        return grid;
    }

    private void UpdateResponsiveGrids()
    {
        var columns = MainContentGrid.ActualWidth >= 760 ? 2 : 1;
        foreach (var grid in ContentPanel.Children.OfType<UniformGrid>().Where(x => Equals(x.Tag, "ResponsiveCards"))) grid.Columns = columns;
    }

    private void AttachHoverInfo(FrameworkElement element, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        element.MouseEnter += (_, e) => StartHoverInfoDelay(element, text, e);
        element.MouseMove += (_, e) => UpdateHoverInfoPosition(e);
        element.MouseLeave += (_, _) => StartHoverInfoHideDelay(element);
        element.Unloaded += (_, _) =>
        {
            if (ReferenceEquals(_currentHoverTarget, element))
                HideHoverInfo();
        };
    }

    private void StartHoverInfoDelay(FrameworkElement target, string text, MouseEventArgs e)
    {
        _hoverShowTimer.Stop();

        var targetChanged = _currentHoverTarget is not null && !ReferenceEquals(_currentHoverTarget, target);
        var hoverVisualStillOpen = HoverInfoPopup.Visibility == Visibility.Visible && _isHoverInfoVisible;
        var recentVisibleSwitch = _recentVisibleHoverClosed && DateTime.UtcNow - _lastVisibleHoverClosedAt <= HoverSwitchGracePeriod;
        var switchFromVisibleCard = (targetChanged && hoverVisualStillOpen) || recentVisibleSwitch;
        var generation = ++_hoverGeneration;

        if (targetChanged && hoverVisualStillOpen)
            HideHoverInfoAnimated(generation, clearState: false);

        _recentVisibleHoverClosed = false;
        _currentHoverTarget = target;
        _pendingHoverText = text;
        _lastHoverMousePosition = e.GetPosition(WindowRootGrid);
        _hoverShowTimer.Interval = switchFromVisibleCard ? HoverSwitchDelay : HoverInitialDelay;
        _hoverShowTimer.Start();
    }

    private void UpdateHoverInfoPosition(MouseEventArgs e)
    {
        _lastHoverMousePosition = e.GetPosition(WindowRootGrid);
        if (_isHoverInfoVisible)
            MoveHoverInfo(_lastHoverMousePosition);
    }

    private void StartHoverInfoHideDelay(FrameworkElement target)
    {
        if (!ReferenceEquals(_currentHoverTarget, target)) return;
        _hoverShowTimer.Stop();
        var wasVisible = _isHoverInfoVisible || HoverInfoPopup.Visibility == Visibility.Visible;
        if (wasVisible)
        {
            _recentVisibleHoverClosed = true;
            _lastVisibleHoverClosedAt = DateTime.UtcNow;
        }
        HideHoverInfoAnimated(++_hoverGeneration, clearState: true);
    }

    private void ShowHoverInfoNow()
    {
        _hoverShowTimer.Stop();
        if (_currentHoverTarget is null || string.IsNullOrWhiteSpace(_pendingHoverText)) return;
        if (!_currentHoverTarget.IsMouseOver) return;

        var generation = ++_hoverGeneration;
        HoverInfoText.Text = _pendingHoverText;
        HoverInfoPopup.Visibility = Visibility.Visible;
        _isHoverInfoVisible = true;
        _recentVisibleHoverClosed = false;
        HoverInfoPopup.UpdateLayout();
        MoveHoverInfo(_lastHoverMousePosition);
        FadeHoverInfo(to: 1, HoverFadeInDuration, generation);
    }

    private void MoveHoverInfo(Point pointer)
    {
        if (!_isHoverInfoVisible || HoverInfoPopup.Visibility != Visibility.Visible) return;

        const double offsetX = 14;
        const double offsetY = 18;
        const double edgePadding = 10;
        HoverInfoPopup.UpdateLayout();

        var popupWidth = HoverInfoPopup.ActualWidth > 0 ? HoverInfoPopup.ActualWidth : HoverInfoPopup.DesiredSize.Width;
        var popupHeight = HoverInfoPopup.ActualHeight > 0 ? HoverInfoPopup.ActualHeight : HoverInfoPopup.DesiredSize.Height;
        var x = pointer.X + offsetX;
        var y = pointer.Y + offsetY;

        if (x + popupWidth + edgePadding > WindowRootGrid.ActualWidth)
            x = pointer.X - popupWidth - offsetX;
        if (y + popupHeight + edgePadding > WindowRootGrid.ActualHeight)
            y = pointer.Y - popupHeight - offsetY;

        HoverInfoTransform.X = Math.Max(edgePadding, x);
        HoverInfoTransform.Y = Math.Max(edgePadding, y);
    }

    private void HideHoverInfo()
    {
        _hoverShowTimer.Stop();
        ++_hoverGeneration;
        _recentVisibleHoverClosed = false;
        _lastVisibleHoverClosedAt = DateTime.MinValue;
        ClearHoverInfoState();
    }

    private void HideHoverInfoAnimated(int generation, bool clearState)
    {
        if (HoverInfoPopup.Visibility != Visibility.Visible)
        {
            if (clearState) ClearHoverInfoState();
            return;
        }

        _isHoverInfoVisible = false;
        FadeHoverInfo(to: 0, HoverFadeOutDuration, generation, () =>
        {
            if (generation != _hoverGeneration) return;
            HoverInfoPopup.Visibility = Visibility.Collapsed;
            if (clearState) ClearHoverInfoState();
        });
    }

    private void FadeHoverInfo(double to, TimeSpan duration, int generation, Action? completed = null)
    {
        HoverInfoPopup.BeginAnimation(OpacityProperty, null);
        var animation = new DoubleAnimation
        {
            From = HoverInfoPopup.Opacity,
            To = to,
            Duration = new Duration(duration),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        animation.Completed += (_, _) =>
        {
            if (generation == _hoverGeneration)
                completed?.Invoke();
        };
        HoverInfoPopup.BeginAnimation(OpacityProperty, animation);
    }

    private void ClearHoverInfoState()
    {
        HoverInfoPopup.BeginAnimation(OpacityProperty, null);
        HoverInfoPopup.Visibility = Visibility.Collapsed;
        HoverInfoPopup.Opacity = 0;
        HoverInfoText.Text = string.Empty;
        _pendingHoverText = null;
        _currentHoverTarget = null;
        _isHoverInfoVisible = false;
    }

    private Border CardBorder()
    {
        var card = new Border
        {
            Background = BrushOf("Panel2"),
            BorderBrush = BrushOf("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 12, 12)
        };
        return card;
    }

    private Button SmallButton(string text, Action action, double minWidth = 96)
    {
        var button = new Button { Content = text, MinWidth = minWidth, Height = 36, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(14, 7, 14, 7) };
        button.Click += (_, _) => action();
        return button;
    }

    private Button SmallButton(string text, Func<Task> action, double minWidth = 96)
    {
        var button = new Button { Content = text, MinWidth = minWidth, Height = 36, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(14, 7, 14, 7) };
        button.Click += async (_, _) => await action();
        return button;
    }

    private void AddActionButton(string text, Action action, bool primary = false)
    {
        var button = new Button { Content = text, Margin = new Thickness(6, 3, 0, 3), MinWidth = 104, Height = 38, Padding = new Thickness(10, 7, 10, 7), FontSize = 14, FontWeight = FontWeights.SemiBold };
        if (primary) button.Style = (Style)Application.Current.Resources["PrimaryButtonStyle"];
        button.Click += (_, _) => action();
        PageActionPanel.Children.Add(button);
    }

    private void AddActionButton(string text, Func<Task> action, bool primary = false)
    {
        var button = new Button { Content = text, Margin = new Thickness(6, 3, 0, 3), MinWidth = 104, Height = 38, Padding = new Thickness(10, 7, 10, 7), FontSize = 14, FontWeight = FontWeights.SemiBold };
        if (primary) button.Style = (Style)Application.Current.Resources["PrimaryButtonStyle"];
        button.Click += async (_, _) => await action();
        PageActionPanel.Children.Add(button);
    }

    private async Task RunGuardedAsync(Func<Task> action)
    {
        var page = _active;
        var renderVersion = _renderVersion;
        try { await action(); }
        catch (Exception ex)
        {
            if (CanApplyPageUi(page, renderVersion))
            {
                MessageBox.Show(ex.Message, "操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusText.Text = ex.Message;
            }
            LogService.Write($"操作失败：{ex}");
        }
    }

    private async Task RestoreAllAsync()
    {
        var renderVersion = _renderVersion;
        await RunGuardedAsync(async () =>
        {
            UpdateComponentProgressIfActive("系统优化", renderVersion, "正在扫描真实系统状态...");
            var plan = await _optimizer.BuildRestorePlanAsync();
            if (!CanApplyPageUi("系统优化", renderVersion)) return;
            ShowRestorePlanDrawer(plan);
            StatusText.Text = $"扫描完成：{plan.ChangeCount} 项需要处理";
        });
    }

    private void ShowRestorePlanDrawer(RestorePlan plan)
    {
        var content = new StackPanel();
        content.Children.Add(new Border
        {
            Background = BrushOf("BlueSoft"), CornerRadius = new CornerRadius(14), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 14),
            Child = new TextBlock { Text = $"扫描了 {plan.Items.Count} 个项目，其中 {plan.ChangeCount} 个将执行安全默认恢复。恢复前会额外创建应急快照。", TextWrapping = TextWrapping.Wrap, LineHeight = 21 }
        });
        foreach (var item in plan.Items)
        {
            var card = new Border { Background = BrushOf("Panel2"), BorderBrush = BrushOf("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10) };
            var panel = new StackPanel();
            var row = new DockPanel { LastChildFill = true };
            var badge = StateBadge(item.CurrentState); DockPanel.SetDock(badge, Dock.Right); row.Children.Add(badge);
            row.Children.Add(new TextBlock { Text = item.Title, FontWeight = FontWeights.SemiBold, FontSize = 14 });
            panel.Children.Add(row);
            panel.Children.Add(new TextBlock { Text = item.Detail, Foreground = BrushOf("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 0), LineHeight = 19 });
            card.Child = panel;
            content.Children.Add(card);
        }
        OpenDrawer("恢复预检", "恢复不依赖本地 Applied 记录；外部策略和无法推导的原值会明确跳过或标记为部分恢复。", content, plan.ChangeCount > 0 ? "恢复所列项目" : "无需恢复", plan.ChangeCount > 0 ? async () => await ExecuteRestorePlanAsync(plan) : null);
    }

    private async Task ExecuteRestorePlanAsync(RestorePlan plan)
    {
        EnsureAdminOrThrow();
        DrawerPrimaryButton.IsEnabled = false;
        DrawerCancelButton.IsEnabled = false;
        DrawerPrimaryButton.Content = "正在恢复...";
        StatusText.Text = "正在执行安全默认恢复...";
        try
        {
            var report = await _optimizer.RestoreDefaultsAsync(plan);
            ShowRestoreResultsDrawer(report);
            StatusText.Text = report.Ok ? "默认恢复完成" : "默认恢复包含部分失败";
            if (_active == "系统优化") await SelectPageAsync("系统优化");
        }
        finally
        {
            DrawerPrimaryButton.IsEnabled = true;
            DrawerCancelButton.IsEnabled = true;
        }
    }

    private void ShowRestoreResultsDrawer(RestoreExecutionReport report)
    {
        var content = new StackPanel();
        foreach (var item in report.Items)
        {
            var color = item.Status is RestoreItemStatus.Restored or RestoreItemStatus.AlreadyDefault ? "Green" : item.Status is RestoreItemStatus.Failed ? "Red" : "Yellow";
            var card = new Border { Background = BrushOf("Panel2"), BorderBrush = BrushOf(color), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10) };
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = $"{item.Title}  ·  {RestoreStatusText(item.Status)}", Foreground = BrushOf(color), FontWeight = FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = item.Detail, Foreground = BrushOf("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), FontSize = 12 });
            card.Child = panel;
            content.Children.Add(card);
        }
        OpenDrawer(report.Ok ? "恢复完成" : "恢复结果需要关注", $"详细报告已保存：{report.ReportPath}", content, "关闭", null);
    }

    private void ShowRollbackResultsDrawer(RollbackExecutionReport report, OptimizerItem item)
    {
        var content = new StackPanel();
        foreach (var target in report.Targets)
        {
            var color = target.Verified ? "Green" : "Yellow";
            var card = new Border { Background = BrushOf("Panel2"), BorderBrush = BrushOf(color), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10) };
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = $"{target.Kind} · {(target.Verified ? "已验证恢复" : "未恢复")}", Foreground = BrushOf(color), FontWeight = FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = target.Target, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), FontSize = 11, Margin = new Thickness(0, 6, 0, 0) });
            panel.Children.Add(new TextBlock { Text = $"期望：{target.Expected}\n当前：{target.Actual}", Foreground = BrushOf("Muted"), TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 6, 0, 0), LineHeight = 19 });
            if (!target.Verified)
                panel.Children.Add(new TextBlock { Text = string.Join("\n", new[] { target.Error, target.Suggestion }.Where(x => !string.IsNullOrWhiteSpace(x))), Foreground = BrushOf("Yellow"), TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 7, 0, 0), LineHeight = 19 });
            card.Child = panel;
            content.Children.Add(card);
        }
        var title = report.Outcome switch
        {
            RollbackOutcome.RestoredToSnapshot => "已恢复到操作前状态",
            RollbackOutcome.RestartRequired => "回退需要重启",
            RollbackOutcome.ExternallyReapplied => "设置被外部重新写入",
            RollbackOutcome.Partial => "回退仅部分完成",
            _ => "回退失败"
        };
        var subtitle = report.Ok
            ? $"所有快照目标均已通过实时验证。若卡片仍显示部分优化，表示该值在本次操作前已经存在。报告：{report.ReportPath}"
            : $"未恢复目标仍保留在状态账本中，不会显示整体成功。报告：{report.ReportPath}";
        Func<Task>? retryAction = report.Ok ? null : async () =>
        {
            EnsureAdminOrThrow();
            StatusText.Text = $"正在重试失败目标：{item.Title}";
            var retry = await _optimizer.RestoreAsync(item.Id);
            ShowRollbackResultsDrawer(retry, item);
            StatusText.Text = retry.Ok ? $"已验证回退：{item.Title}" : $"回退仍未完成：{item.Title}";
        };
        OpenDrawer(title, subtitle, content, report.Ok ? "关闭" : "重试失败项", retryAction,
            report.Ok ? "继续恢复安全默认" : null, report.Ok ? async () =>
            {
                StatusText.Text = $"正在生成安全默认恢复预检：{item.Title}";
                var plan = await _optimizer.BuildRestorePlanAsync(new[] { item.Id });
                ShowRestorePlanDrawer(plan);
                StatusText.Text = "安全默认恢复预检已完成";
            } : null);
    }

    private static string RestoreStatusText(RestoreItemStatus status) => status switch
    {
        RestoreItemStatus.Restored => "已恢复",
        RestoreItemStatus.AlreadyDefault => "已经默认",
        RestoreItemStatus.Partial => "部分恢复",
        RestoreItemStatus.Skipped => "已跳过",
        RestoreItemStatus.Failed => "失败",
        _ => "需要重启"
    };

    private void ShowOptimizerStateDrawer(OptimizerItem item, OptimizationStateInfo live)
    {
        var content = new StackPanel();
        content.Children.Add(StateBadge(live.State, live.SkippedTargets));
        content.Children.Add(new TextBlock { Text = live.Detail, TextWrapping = TextWrapping.Wrap, LineHeight = 22, Margin = new Thickness(0, 14, 0, 12) });
        AddOptimizationTargetGroup(content, "已优化部分", live.TargetDetails.Where(x => x.State == OptimizationTargetState.Optimized), "Green", item, true);
        AddOptimizationTargetGroup(content, "未优化／默认部分", live.TargetDetails.Where(x => x.State is OptimizationTargetState.Default or OptimizationTargetState.Unoptimized), "Blue", item, true);
        AddOptimizationTargetGroup(content, "用户已跳过", live.TargetDetails.Where(x => x.Applicability == TargetApplicability.UserSkipped), "Muted", item, true);
        AddOptimizationTargetGroup(content, "本机不适用", live.TargetDetails.Where(x => x.Applicability is TargetApplicability.NotApplicable or TargetApplicability.OptionalMissing or TargetApplicability.UnsupportedBuild), "Yellow");
        AddOptimizationTargetGroup(content, "安全跳过", live.TargetDetails.Where(x => x.Applicability == TargetApplicability.Unsafe || x.State == OptimizationTargetState.Skipped && x.Applicability != TargetApplicability.UserSkipped), "Yellow");
        AddOptimizationTargetGroup(content, "读取失败／需要确认", live.TargetDetails.Where(x => x.Applicability == TargetApplicability.QueryFailed || x.State is OptimizationTargetState.Unknown or OptimizationTargetState.Diverged or OptimizationTargetState.ExternallyManaged), "Yellow");
        if (live.State == OptimizationLiveState.Mixed)
            content.Children.Add(new TextBlock { Text = "恢复会将该项目撤销到当前 Windows 的安全默认基线，并在执行前创建应急快照。它不会依赖 Applied 记录，但无法保证保留第三方程序写入的自定义值。", Foreground = BrushOf("Muted"), TextWrapping = TextWrapping.Wrap, LineHeight = 21, Margin = new Thickness(0, 10, 0, 0) });
        else if (live.State is OptimizationLiveState.Unknown or OptimizationLiveState.ExternallyManaged)
            content.Children.Add(new TextBlock { Text = "当前存在无法确认或外部管理的目标，已禁止通过普通开关盲目修改。", Foreground = BrushOf("Muted"), TextWrapping = TextWrapping.Wrap, LineHeight = 21, Margin = new Thickness(0, 10, 0, 0) });
        Func<Task>? primaryAction = null;
        var primaryText = "关闭";
        if (live.State == OptimizationLiveState.Mixed)
        {
            var remaining = live.TargetDetails.Count(x => x.Applicability == TargetApplicability.Applicable && x.State is OptimizationTargetState.Default or OptimizationTargetState.Unoptimized);
            primaryText = $"优化剩余 {remaining} 项";
            primaryAction = async () =>
            {
                if (!_busyOptimizerIds.Add(item.Id)) return;
                _optimizerOperationText[item.Id] = "补全优化中";
                string? completionText = null;
                try
                {
                    EnsureAdminOrThrow();
                    DrawerPrimaryButton.IsEnabled = false;
                    DrawerSecondaryButton.IsEnabled = false;
                    StatusText.Text = $"正在生成剩余目标计划：{item.Title}";
                    var plan = await _optimizer.BuildApplyPlanAsync(item.Id, ApplyMode.RemainingOnly);
                    await CloseDrawerAsync();
                    if (_active == "系统优化") await SelectPageAsync("系统优化");
                    var message = await _optimizer.ApplyRemainingAsync(plan, _shutdown.Token);
                    completionText = message;
                }
                finally
                {
                    _busyOptimizerIds.Remove(item.Id);
                    _optimizerOperationText.Remove(item.Id);
                    DrawerPrimaryButton.IsEnabled = true;
                    DrawerSecondaryButton.IsEnabled = true;
                    if (_active == "系统优化")
                    {
                        try { await SelectPageAsync("系统优化"); }
                        catch (Exception refreshError) { LogService.Write($"补全优化结束后刷新界面失败：{refreshError}"); }
                    }
                    else _pageCache.Remove("系统优化");
                    if (!string.IsNullOrWhiteSpace(completionText)) StatusText.Text = completionText;
                }
            };
            OpenDrawer(item.Title, "逐项目实时系统状态", content, primaryText, primaryAction,
                "恢复安全默认", async () =>
                {
                    StatusText.Text = $"正在生成单项目恢复预检：{item.Title}";
                    var restorePlan = await _optimizer.BuildRestorePlanAsync(new[] { item.Id });
                    ShowRestorePlanDrawer(restorePlan);
                    StatusText.Text = "单项目恢复预检已完成";
                });
            return;
        }
        else if (live.State == OptimizationLiveState.OptimizedWithSkips)
        {
            var systemSkipped = live.TargetDetails.Count(x => x.Applicability is TargetApplicability.NotApplicable or TargetApplicability.OptionalMissing or TargetApplicability.Unsafe or TargetApplicability.UnsupportedBuild);
            if (systemSkipped > 0)
            {
                primaryText = $"确认系统跳过 {systemSkipped} 项";
                primaryAction = async () =>
                {
                    var count = await _optimizer.ConfirmSkippedTargetsAsync(item);
                    await CloseDrawerAsync();
                    StatusText.Text = $"已记录 {count} 个系统跳过目标；用户跳过项保持不变";
                };
            }
        }
        OpenDrawer(item.Title, "逐目标实时系统状态", content, primaryText, primaryAction);
    }

    private void AddOptimizationTargetGroup(StackPanel host, string title, IEnumerable<OptimizationTargetInfo> source, string color, OptimizerItem? item = null, bool allowUserSkip = false)
    {
        var targets = source.ToList();
        if (targets.Count == 0) return;
        host.Children.Add(new TextBlock { Text = $"{title} · {targets.Count}", Foreground = BrushOf(color), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 8) });
        foreach (var target in targets)
        {
            var userSkipped = target.Applicability == TargetApplicability.UserSkipped;
            var card = new Border { Background = BrushOf(userSkipped ? "Panel3" : "Panel2"), BorderBrush = BrushOf("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(13), Margin = new Thickness(0, 0, 0, 8), Opacity = userSkipped ? 0.68 : 1 };
            var panel = new StackPanel();
            var header = new DockPanel { LastChildFill = true };
            if (allowUserSkip && item != null && (userSkipped || target.Applicability == TargetApplicability.Applicable && target.State is OptimizationTargetState.Optimized or OptimizationTargetState.Default or OptimizationTargetState.Unoptimized))
            {
                var action = new Button
                {
                    Content = userSkipped ? "取消跳过" : target.State == OptimizationTargetState.Optimized ? "恢复并跳过" : "跳过",
                    Style = (Style)Application.Current.Resources["GhostButtonStyle"],
                    MinHeight = 28,
                    Padding = new Thickness(10, 3, 10, 3),
                    FontSize = 11,
                    Margin = new Thickness(10, 0, 0, 0)
                };
                DockPanel.SetDock(action, Dock.Right);
                action.Click += async (_, e) =>
                {
                    e.Handled = true;
                    var originalText = action.Content;
                    action.IsEnabled = false;
                    action.Content = userSkipped ? "恢复中..." : target.State == OptimizationTargetState.Optimized ? "恢复中..." : "保存中...";
                    try { await RunGuardedAsync(async () => await ExecuteTargetSkipAsync(item, target, userSkipped)); }
                    finally
                    {
                        if (action.IsVisible)
                        {
                            action.Content = originalText;
                            action.IsEnabled = true;
                        }
                    }
                };
                header.Children.Add(action);
            }
            header.Children.Add(new TextBlock { Text = userSkipped ? $"{target.Kind} · 已跳过" : $"{target.Kind} · {OptimizationTargetStateText(target.State)}", Foreground = BrushOf(userSkipped ? "Muted" : OptimizationTargetStateColor(target.State)), FontWeight = FontWeights.SemiBold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(header);
            panel.Children.Add(new TextBlock { Text = target.Target, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), FontSize = 11, Margin = new Thickness(0, 5, 0, 0) });
            panel.Children.Add(new TextBlock { Text = target.Detail, Foreground = BrushOf("Muted"), TextWrapping = TextWrapping.Wrap, FontSize = 11, LineHeight = 18, Margin = new Thickness(0, 5, 0, 0) });
            card.Child = panel;
            host.Children.Add(card);
        }
    }

    private async Task ExecuteTargetSkipAsync(OptimizerItem item, OptimizationTargetInfo target, bool removeSkip)
    {
        if (!_busyOptimizerIds.Add(item.Id)) return;
        _optimizerOperationText[item.Id] = removeSkip ? "取消跳过中" : target.State == OptimizationTargetState.Optimized ? "单项恢复中" : "设置跳过中";
        var renderVersion = _renderVersion;
        TargetSkipResult result;
        try
        {
            if (!removeSkip && target.State == OptimizationTargetState.Optimized) EnsureAdminOrThrow();
            UpdateComponentProgressIfActive("系统优化", renderVersion, $"后台处理：{item.Title} / {target.Target}");
            result = removeSkip
                ? await _optimizer.RemoveTargetSkipAsync(item, target)
                : await _optimizer.SkipTargetAsync(item, target);
            UpdateComponentProgressIfActive("系统优化", renderVersion, result.Message);
        }
        finally
        {
            _busyOptimizerIds.Remove(item.Id);
            _optimizerOperationText.Remove(item.Id);
        }
        var refreshed = (await _optimizer.GetLiveStatesAsync(new[] { item }, forceRefresh: true))[item.Id];
        if (!CanApplyPageUi("系统优化", renderVersion)) return;
        ReplaceOptimizerCardInPlace(item, refreshed);
        ShowOptimizerStateDrawer(item, refreshed);
    }

    private void ReplaceOptimizerCardInPlace(OptimizerItem item, OptimizationStateInfo live)
    {
        if (_active != "系统优化") return;
        var grid = ContentPanel.Children.OfType<UniformGrid>().FirstOrDefault(x => x.Uid == "OptimizerCards");
        if (grid == null) return;
        var oldCard = grid.Children.OfType<Border>().FirstOrDefault(x => item.Id.Equals(x.Tag as string, StringComparison.OrdinalIgnoreCase));
        if (oldCard == null) return;
        var index = grid.Children.IndexOf(oldCard);
        grid.Children.RemoveAt(index);
        grid.Children.Insert(index, CreateOptimizerCard(item, live, true));
    }

    private static string OptimizationTargetStateText(OptimizationTargetState state) => state switch
    {
        OptimizationTargetState.Optimized => "已优化",
        OptimizationTargetState.Default => "默认／未优化",
        OptimizationTargetState.Unoptimized => "未优化（保留原值）",
        OptimizationTargetState.Diverged => "其他值",
        OptimizationTargetState.Unavailable => "不可用",
        OptimizationTargetState.Skipped => "安全跳过",
        OptimizationTargetState.ExternallyManaged => "外部策略",
        _ => "无法确认"
    };

    private static string OptimizationTargetStateColor(OptimizationTargetState state) => state switch
    {
        OptimizationTargetState.Optimized => "Green",
        OptimizationTargetState.Default => "Blue",
        OptimizationTargetState.Unoptimized => "Blue",
        OptimizationTargetState.Skipped => "Yellow",
        OptimizationTargetState.ExternallyManaged => "Blue",
        _ => "Yellow"
    };

    private void OpenDrawer(string title, string subtitle, UIElement content, string primaryText, Func<Task>? primaryAction,
        string? secondaryText = null, Func<Task>? secondaryAction = null)
    {
        var alreadyOpen = DrawerOverlay.Visibility == Visibility.Visible;
        var scrollOffset = alreadyOpen ? DrawerScrollViewer.VerticalOffset : 0;
        if (!alreadyOpen) _drawerPreviousFocus = Keyboard.FocusedElement;
        _drawerPrimaryAction = primaryAction;
        _drawerSecondaryAction = secondaryAction;
        DrawerTitle.Text = title;
        DrawerSubtitle.Text = subtitle;
        DrawerContent.Children.Clear();
        DrawerContent.Children.Add(content);
        DrawerPrimaryButton.Content = primaryText;
        DrawerPrimaryButton.Visibility = primaryAction == null && primaryText == "关闭" ? Visibility.Collapsed : Visibility.Visible;
        DrawerSecondaryButton.Content = secondaryText ?? "";
        DrawerSecondaryButton.Visibility = secondaryAction == null ? Visibility.Collapsed : Visibility.Visible;
        DrawerCancelButton.Content = primaryAction == null ? "关闭" : "取消";
        if (alreadyOpen)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => DrawerScrollViewer.ScrollToVerticalOffset(scrollOffset));
        }
        else
        {
            MotionService.OpenDrawer(DrawerOverlay, DrawerTransform);
            DrawerCancelButton.Focus();
        }
    }

    private async Task CloseDrawerAsync()
    {
        _drawerPrimaryAction = null;
        _drawerSecondaryAction = null;
        await MotionService.CloseDrawerAsync(DrawerOverlay, DrawerTransform);
        if (_drawerPreviousFocus is IInputElement focus) Keyboard.Focus(focus);
    }

    private async void DrawerClose_Click(object sender, RoutedEventArgs e) => await CloseDrawerAsync();
    private async void DrawerBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => await CloseDrawerAsync();
    private async void DrawerPrimary_Click(object sender, RoutedEventArgs e)
    {
        var action = _drawerPrimaryAction;
        if (action == null) { await CloseDrawerAsync(); return; }
        await RunGuardedAsync(action);
    }

    private async void DrawerSecondary_Click(object sender, RoutedEventArgs e)
    {
        var action = _drawerSecondaryAction;
        if (action != null) await RunGuardedAsync(action);
    }

    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DrawerOverlay.Visibility == Visibility.Visible) { e.Handled = true; await CloseDrawerAsync(); }
    }

    private static void EnsureAdminOrThrow()
    {
        if (!AdminService.IsAdministrator) throw new InvalidOperationException("该操作需要管理员权限，请点击“管理员重启”后重试。");
    }

    private void ApplyTheme()
    {
        _pageCache.Clear();
        var dark = _settings.ThemeMode.Equals("Dark", StringComparison.OrdinalIgnoreCase) ||
                   (_settings.ThemeMode.Equals("System", StringComparison.OrdinalIgnoreCase) && !SystemUsesLightTheme());
        if (dark)
        {
            SetBrush("Bg", "#EE101114"); SetBrush("Sidebar", "#E815161A"); SetBrush("Panel", "#F31A1B20"); SetBrush("Panel2", "#F0202126"); SetBrush("Panel3", "#292A30"); SetBrush("Border", "#34353C"); SetBrush("WindowBorder", "#5A5D66"); SetBrush("Text", "#F5F5F7"); SetBrush("Muted", "#A4A5AD"); SetBrush("BlueSoft", "#193653");
        }
        else
        {
            SetBrush("Bg", "#EEF5F5F7"); SetBrush("Sidebar", "#E8ECECF0"); SetBrush("Panel", "#F7FFFFFF"); SetBrush("Panel2", "#F2FFFFFF"); SetBrush("Panel3", "#E8E8ED"); SetBrush("Border", "#D1D1D6"); SetBrush("WindowBorder", "#C6C6CC"); SetBrush("Text", "#1D1D1F"); SetBrush("Muted", "#6E6E73"); SetBrush("BlueSoft", "#DDEEFF");
        }
        SetBrush("Blue", "#0A84FF"); SetBrush("Green", "#30D158"); SetBrush("Red", "#FF453A"); SetBrush("Yellow", "#FFD60A");
        if (IsLoaded) ApplyWindowRegion();
    }

    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 1)) == 1;
        }
        catch { return false; }
    }

    private static void SetBrush(string key, string color) => Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    private static Brush BrushOf(string key) => (Brush)Application.Current.Resources[key];
    private static string RiskText(string risk) => risk == "high" ? "高风险" : "普通项目";
    private static string TrimOutput(string text) => text.Length > 5000 ? text[..5000] + "\n\n输出较长，已截断显示，完整内容请查看日志。" : text;

    private void InitializeWindowRegion()
    {
        var chrome = WindowChrome.GetWindowChrome(this);
        if (chrome != null) chrome.CornerRadius = new CornerRadius(0);
        WindowFrameBorder.CornerRadius = new CornerRadius(0);
        SidebarBorder.CornerRadius = new CornerRadius(0);
        _windowSource = PresentationSource.FromVisual(this) as HwndSource;
        _windowSource?.AddHook(WindowMessageHook);
        ApplyWindowRegion();
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmDpiChanged)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ApplyWindowRegion);
        return IntPtr.Zero;
    }

    private void ApplyWindowRegion()
    {
        if (_windowSource == null) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var bounds)) return;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            // Windows 11 supplies a GPU-antialiased round corner and native
            // border. Do not combine it with a GDI region or WPF outline.
            _ = SetWindowRgn(hwnd, IntPtr.Zero, true);
            var round = DwmRoundCorners;
            _ = DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref round, sizeof(int));
            var borderColor = NativeColor(BrushOf("WindowBorder"));
            _ = DwmSetWindowAttribute(hwnd, DwmBorderColor, ref borderColor, sizeof(int));
            WindowOutlineBorder.Visibility = Visibility.Collapsed;
            return;
        }

        WindowOutlineBorder.Visibility = Visibility.Visible;
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0) return;

        var dpi = GetDpiForWindow(hwnd);
        if (dpi == 0) dpi = 96;
        var diameter = WindowState == WindowState.Maximized
            ? 0
            : Math.Max(1, (int)Math.Round(RoundedWindowRadiusDip * dpi / 48d));
        var region = diameter == 0
            ? CreateRectRgn(0, 0, width + 1, height + 1)
            : CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter);
        if (region == IntPtr.Zero) return;
        if (SetWindowRgn(hwnd, region, true) == 0)
            _ = DeleteObject(region);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rectangle);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr objectHandle);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    private static int NativeColor(Brush brush)
    {
        var color = brush is SolidColorBrush solid ? solid.Color : Colors.Gray;
        return color.R | color.G << 8 | color.B << 16;
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e) => MorePopup.IsOpen = true;

    private void ExportLogs_Click(object sender, RoutedEventArgs e)
    {
        var path = LogService.ExportToDesktop();
        MessageBox.Show($"日志已导出到桌面：\n{path}", "导出日志", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void About_Click(object sender, RoutedEventArgs e)
    {
        var status = await _optimizer.GetComponentStatusAsync();
        var about = new Window
        {
            Title = "关于软件",
            Owner = this,
            Width = 720,
            Height = 760,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = BrushOf("Bg"),
            Foreground = BrushOf("Text")
        };
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = new StackPanel { Margin = new Thickness(26) };
        panel.Children.Add(new TextBlock { Text = "系统优化工具", FontSize = 30, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text = "Windows 本地优化、运行环境修复与驱动维护工具", Foreground = BrushOf("Muted"), FontSize = 16, Margin = new Thickness(0, 12, 0, 18) });
        panel.Children.Add(AboutLine("软件版本", "V3.1"));
        panel.Children.Add(AboutLine("组件状态", status.State == ComponentState.Online ? $"OptimizerNXT {OptimizerService.PinnedVersion} · YAML {status.ReadyCount}/{status.TotalCount}" : "OptimizerNXT 未就绪"));
        panel.Children.Add(AboutLine("软件作者", "Yangg"));
        panel.Children.Add(AboutLine("适用系统", "Windows 10 / Windows 11"));

        panel.Children.Add(AboutSectionTitle("软件简介"));
        panel.Children.Add(AboutCard(AboutParagraph("本软件面向 Windows 用户，提供系统优化、环境修复、常用组件维护、驱动辅助管理和系统清理等功能。工具以本地桌面端形式运行，适合重装系统后初始化电脑、日常维护系统状态，以及快速修复部分软件或游戏运行环境缺失问题。")));

        panel.Children.Add(AboutSectionTitle("使用建议"));
        panel.Children.Add(AboutCard(AboutBullets(
            "重装系统后，建议优先检查驱动环境、运行库组件和 DirectX 组件是否完整。",
            "系统优化功能建议按需启用，不建议一次性开启不了解作用的高风险项目。",
            "执行优化前，建议关闭正在运行的大型软件，并确认当前系统状态正常。",
            "如果优化后出现异常，可以通过恢复默认或回退功能还原相关配置。",
            "遇到问题时，可优先查看帮助文档中的常见案例与处理方法。")));

        panel.Children.Add(AboutSectionTitle("开源组件"));
        panel.Children.Add(AboutCard(AboutBullets(
            "OptimizerNXT：用于提供部分 Windows 优化策略与 YAML 配置支持。",
            "BleachBit：用于提供系统清理服务。",
            "本工具对相关功能进行了桌面端整合与轻量化封装。")));

        panel.Children.Add(AboutSectionTitle("开源地址"));
        panel.Children.Add(AboutCard(AboutLink("https://github.com/yancy520666/system-optimizer")));

        panel.Children.Add(AboutSectionTitle("免责声明"));
        panel.Children.Add(AboutCard(AboutParagraph("本软件仅作为 Windows 系统功能管理、运行环境修复和系统维护辅助工具使用。不同电脑配置、系统版本、驱动状态和第三方环境可能导致不同结果。使用高风险优化项前，请确认已了解相关功能作用。因误操作、系统环境异常或第三方组件导致的问题，开发者不承担直接责任。")));

        scroll.Content = panel;
        about.Content = scroll;
        about.ShowDialog();
    }

    private UIElement AboutLine(string label, string value)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = label, Foreground = BrushOf("Muted"), FontSize = 16 });
        var v = new TextBlock { Text = value, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(v, 1);
        row.Children.Add(v);
        return row;
    }

    private UIElement AboutSectionTitle(string text) => new TextBlock
    {
        Text = text,
        FontSize = 18,
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(0, 20, 0, 8)
    };

    private Border AboutCard(UIElement content)
    {
        var card = CardBorder();
        card.Margin = new Thickness(0, 0, 0, 4);
        card.Padding = new Thickness(14);
        card.Child = content;
        return card;
    }

    private TextBlock AboutParagraph(string text) => new()
    {
        Text = text,
        Foreground = BrushOf("Muted"),
        FontSize = 14.5,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = 25
    };

    private StackPanel AboutBullets(params string[] items)
    {
        var panel = new StackPanel();
        foreach (var item in items)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "• " + item,
                Foreground = BrushOf("Muted"),
                FontSize = 14.5,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 25,
                Margin = new Thickness(0, 0, 0, 5)
            });
        }
        return panel;
    }

    private TextBlock AboutLink(string url)
    {
        var block = new TextBlock
        {
            FontSize = 14.5,
            Foreground = BrushOf("Muted"),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 26
        };
        var link = new Hyperlink(new Run(url))
        {
            NavigateUri = new Uri(url),
            Foreground = BrushOf("Blue")
        };
        link.RequestNavigate += (_, e) =>
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        };
        block.Inlines.Add(link);
        return block;
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(HelpDocumentUrl) { UseShellExecute = true });
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) ToggleMaximize();
        else DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
