using System;
using System.Globalization;
using System.Resources;

namespace DiskAlanAnaliz.Helpers
{
    public static class LocalizationHelper
    {
        private static ResourceManager _resourceManager;
        private static CultureInfo _currentCulture;

        static LocalizationHelper()
        {
            _resourceManager = new ResourceManager("DiskAlanAnaliz.Resources.Strings", typeof(LocalizationHelper).Assembly);
            _currentCulture = CultureInfo.CurrentCulture;
        }

        public static string GetString(string key)
        {
            try
            {
                return _resourceManager.GetString(key, _currentCulture) ?? key;
            }
            catch
            {
                return key;
            }
        }

        public static void SetCulture(CultureInfo culture)
        {
            _currentCulture = culture;
        }
    }
}