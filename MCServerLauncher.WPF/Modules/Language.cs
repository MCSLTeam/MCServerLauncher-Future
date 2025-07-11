using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Threading;

namespace MCServerLauncher.WPF.Modules
{
    public class Lang : INotifyPropertyChanged
    {
        private readonly ResourceManager _resourceManager = new("MCServerLauncher.WPF.Translations.Lang", typeof(Lang).Assembly);
        private static readonly Lazy<Lang> Lazy = new(() => new Lang());
        public static Lang Tr => Lazy.Value;
        public event PropertyChangedEventHandler? PropertyChanged;

        public string this[string name]
        {
            get
            {
                if (name == null)
                {
                    throw new ArgumentNullException(nameof(name));
                }
                return _resourceManager.GetString(name)!.Replace("\\n", "\n");
            }
        }

        public void ChangeLanguage(CultureInfo cultureInfo)
        {
            CultureInfo.CurrentCulture = cultureInfo;
            CultureInfo.CurrentUICulture = cultureInfo;
            Thread.CurrentThread.CurrentCulture = cultureInfo;
            Thread.CurrentThread.CurrentUICulture = cultureInfo;

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("item[]"));
        }

        public static readonly List<string?> LanguageList = new()
        {
            "en-US",
            "ja-JP",
            "ru-RU",
            "zh-CN",
            "zh-HK",
            "zh-TW",
        };

        public static readonly List<string> LanguageNameList = new()
        {
            "English (US)",
            "日本語",
            "Русский",
            "简体中文 (中国)",
            "繁體中文 (中国香港)",
            "正體中文 (中国台湾)",
        };
    }
}
