using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;

namespace OneMMC.Services
{
    public class NavigationService : INavigationService
    {
        private readonly Frame _frame;
        private readonly Dictionary<string, Type> _pageMap;

        public NavigationService(Frame frame)
        {
            _frame = frame;
            _pageMap = new Dictionary<string, Type>
            {
                { "PCManagement", typeof(Views.PCManagement.PCManagement) },
                { "PolicyPolicies", typeof(Views.PolicyManagement.PolicyManagement) },
                { "CertificatesCredential", typeof(Views.CertificatesCredential) },
                { "UserSecurity", typeof(Views.UserSecurityPage) },
                { "PrintManagement", typeof(Views.PrintManagement) },
                { "SystemManagement", typeof(Views.SystemManagement) },
                { "SettingsPage", typeof(Views.SettingsPage) }
            };
        }

        public void Navigate(string pageKey)
        {
            if (_pageMap.TryGetValue(pageKey, out var pageType))
            {
                _frame.Navigate(pageType);
            }
        }

        public void GoBack()
        {
            if (_frame.CanGoBack)
            {
                _frame.GoBack();
            }
        }

    }
}
