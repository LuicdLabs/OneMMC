using OneMMC.Core.Localization;

namespace OneMMC.Localization
{
    /// <summary>
    /// Localized strings for the welcome dialog.
    /// Resources are loaded from Resources.resw file.
    /// </summary>
    public partial class LocalizedStrings
    {
        public string WelcomeDialog_Title => GetResource("WelcomeDialog_Title");
        public string WelcomeDialog_Description => GetResource("WelcomeDialog_Description");
        public string WelcomeDialog_FeaturesHeader => GetResource("WelcomeDialog_FeaturesHeader");
        public string WelcomeDialog_Feature1 => GetResource("WelcomeDialog_Feature1");
        public string WelcomeDialog_Feature2 => GetResource("WelcomeDialog_Feature2");
        public string WelcomeDialog_Feature3 => GetResource("WelcomeDialog_Feature3");
        public string WelcomeDialog_Feature4 => GetResource("WelcomeDialog_Feature4");
        public string WelcomeDialog_Feature5 => GetResource("WelcomeDialog_Feature5");
        public string WelcomeDialog_WarningTitle => GetResource("WelcomeDialog_WarningTitle");
        public string WelcomeDialog_WarningMessage => GetResource("WelcomeDialog_WarningMessage");
        public string WelcomeDialog_RemindAfter30Days => GetResource("WelcomeDialog_RemindAfter30Days");
        public string WelcomeDialog_LetsExplore => GetResource("WelcomeDialog_LetsExplore");
    }
}
