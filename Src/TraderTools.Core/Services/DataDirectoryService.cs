using System;
using System.ComponentModel.Composition;
using System.IO;

namespace TraderTools.Core.Services
{
    [Export(typeof(DataDirectoryService))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class DataDirectoryService
    {
        private string _mainDirectory;
        private string _applicationName = null;
        private string _mainDirectoryWithApplicationName;

        public DataDirectoryService()
        {
            _mainDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"TraderTools");
        }

        public string MainDirectory => _mainDirectory;

        public string MainDirectoryWithApplicationName => _mainDirectoryWithApplicationName;

        public void SetApplicationName(string applicationName)
        {
            _applicationName = applicationName;

            if (!string.IsNullOrEmpty(_applicationName))
            {
                _mainDirectoryWithApplicationName = Path.Combine(_mainDirectory, _applicationName);
            }
            else
            {
                _mainDirectoryWithApplicationName = _mainDirectory;
            }
        }
    }
}