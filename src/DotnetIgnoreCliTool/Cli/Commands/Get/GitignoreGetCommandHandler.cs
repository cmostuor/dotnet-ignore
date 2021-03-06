﻿using DotnetIgnoreCliTool.Github.Models;
using DotnetIgnoreCliTool.Github.Services;
using PowerArgs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DotnetIgnoreCliTool.Cli.Commands.Get.Merge;
using DotnetIgnoreCliTool.Cli.Commands.Get.Names;
using DotnetIgnoreCliTool.Cli.FIles;

namespace DotnetIgnoreCliTool.Cli.Commands.Get
{
    public class GitignoreGetCommandHandler : IApplicationCommandHandler<GitignoreGetCommand>
    {
        private readonly IGitignoreService _gitignoreService;
        private readonly IConcatedNamesProcessor _concatedNamesProcessor;
        private readonly IMergeStrategy _mergeStrategy;
        private readonly IGitignoreFileWriter _gitignoreFileWriter;

        public GitignoreGetCommandHandler(IGitignoreService gitignoreService,
            IConcatedNamesProcessor concatedNamesProcessor,
            IMergeStrategy mergeStrategy,
            IGitignoreFileWriter gitignoreFileWriter)
        {
            _gitignoreService = gitignoreService ?? throw new ArgumentNullException(nameof(gitignoreService));
            _concatedNamesProcessor = concatedNamesProcessor ?? throw new ArgumentNullException(nameof(concatedNamesProcessor));
            _mergeStrategy = mergeStrategy ?? throw new ArgumentNullException(nameof(mergeStrategy));
            _gitignoreFileWriter = gitignoreFileWriter ?? throw new ArgumentNullException(nameof(gitignoreFileWriter));
        }

        public async Task<int> HandleCommandAsync(GitignoreGetCommand command)
        {
            var names = command.NamesOption.Value();
            var gitIgnoreFile = await GetGitIgnoreFileFromGithub(names);

            var destination = command.DestinationOption.Value();
            await SaveGitIgnoreFile(destination, gitIgnoreFile);

            return ReturnCodes.Success;
        }

        private async Task<GitignoreFile> GetGitIgnoreFileFromGithub(string providedNames)
        {
            var names = _concatedNamesProcessor.Process(providedNames);

            var gitIgnoreFiles = await DownloadAllGitIgnoreFiles(names);

            EnsureAllGitIgnoreFilesWhereDownloaded(gitIgnoreFiles, names);

            return _mergeStrategy.Merge(gitIgnoreFiles);
        }

        private async Task<GitignoreFile[]> DownloadAllGitIgnoreFiles(IEnumerable<string> names)
        {
            var gitIgnoreFilesTasks = names
               .Select(name => _gitignoreService.GetIgnoreFile(name))
               .ToArray();

            await Task.WhenAll(gitIgnoreFilesTasks);
            
            return gitIgnoreFilesTasks
               .Select(task => task.Result)
               .ToArray();
        }


        private static void EnsureAllGitIgnoreFilesWhereDownloaded(IReadOnlyCollection<GitignoreFile> gitIgnoreFiles,
            IEnumerable<string> names)
        {
            var isAnyEmptyFile = gitIgnoreFiles
               .Any(file => file == GitignoreFile.Empty);

            if (isAnyEmptyFile)
            {
                var exceptionMessageForFailedFiles = gitIgnoreFiles
                   .Zip(names, (file, name) => (file, name))
                   .Where(tuple => tuple.file == GitignoreFile.Empty)
                   .Select(tuple => $"Name {tuple.name} is not correct .gitignore file name {Environment.NewLine}")
                   .Aggregate(string.Empty, (msg, aggregate) => aggregate + msg);

                throw new ArgException(exceptionMessageForFailedFiles);
            }
        }

        private async Task SaveGitIgnoreFile(string destination, GitignoreFile gitIgnoreFile)
        {
            try
            {
                await _gitignoreFileWriter.WriteToFileAsync(destination, gitIgnoreFile.Content);
            }
            catch (Exception e)
            {
                throw new ArgException($"Destination path {destination} is invalid or there were " +
                                       "problem with file access", e);
            }
        }
    }
}