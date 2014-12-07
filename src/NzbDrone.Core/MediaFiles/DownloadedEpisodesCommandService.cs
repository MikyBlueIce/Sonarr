﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaFiles
{
    public class DownloadedEpisodesCommandService : IExecute<DownloadedEpisodesScanCommand>
    {
        private readonly IDownloadedEpisodesImportService _downloadedEpisodesImportService;
        private readonly IDownloadTrackingService _downloadTrackingService;
        private readonly ICompletedDownloadService _completedDownloadService;
        private readonly IDiskProvider _diskProvider;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public DownloadedEpisodesCommandService(IDownloadedEpisodesImportService downloadedEpisodesImportService,
                                                IDownloadTrackingService downloadTrackingService,
                                                ICompletedDownloadService completedDownloadService,
                                                IDiskProvider diskProvider,
                                                IConfigService configService,
                                                Logger logger)
        {
            _downloadedEpisodesImportService = downloadedEpisodesImportService;
            _downloadTrackingService = downloadTrackingService;
            _completedDownloadService = completedDownloadService;
            _diskProvider = diskProvider;
            _configService = configService;
            _logger = logger;
        }

        private List<ImportResult> ProcessDroneFactoryFolder()
        {
            var downloadedEpisodesFolder = _configService.DownloadedEpisodesFolder;

            if (String.IsNullOrEmpty(downloadedEpisodesFolder))
            {
                _logger.Trace("Drone Factory folder is not configured");
                return new List<ImportResult>();
            }

            if (!_diskProvider.FolderExists(downloadedEpisodesFolder))
            {
                _logger.Warn("Drone Factory folder [{0}] doesn't exist.", downloadedEpisodesFolder);
                return new List<ImportResult>();
            }

            return _downloadedEpisodesImportService.ProcessRootFolder(new DirectoryInfo(downloadedEpisodesFolder));
        }

        private List<ImportResult> ProcessFolder(DownloadedEpisodesScanCommand message)
        {
            if (!_diskProvider.FolderExists(message.Path))
            {
                _logger.Warn("Folder specified for import scan [{0}] doesn't exist.", message.Path);
                return new List<ImportResult>();
            }

            if (message.DownloadClientId.IsNotNullOrWhiteSpace())
            {
                var trackedDownload = _downloadTrackingService.GetQueuedDownloads().Where(v => v.DownloadItem.DownloadClientId == message.DownloadClientId).FirstOrDefault();

                if (trackedDownload == null)
                {
                    _logger.Warn("External directory scan request for unknown download {0}, attempting normal import. [{1}]", message.DownloadClientId, message.Path);

                    return _downloadedEpisodesImportService.ProcessFolder(new DirectoryInfo(message.Path));
                }
                else
                {
                    return _completedDownloadService.Import(trackedDownload, message.Path);
                }
            }
            else
            {
                return _downloadedEpisodesImportService.ProcessFolder(new DirectoryInfo(message.Path));
            }
        }

        public void Execute(DownloadedEpisodesScanCommand message)
        {
            List<ImportResult> importResults;

            if (message.Path.IsNotNullOrWhiteSpace())
            {
                importResults = ProcessFolder(message);
            }
            else
            {
                importResults = ProcessDroneFactoryFolder();
            }

            if (importResults == null || !importResults.Any(v => v.Result == ImportResultType.Imported))
            {
                // Atm we don't report it as a command failure, coz that would cause the download to be failed.
                // Changing the message won't do a thing either, coz it will get set to 'Completed' a msec later.
                //message.SetMessage("Failed to import");
            }
        }
    }
}