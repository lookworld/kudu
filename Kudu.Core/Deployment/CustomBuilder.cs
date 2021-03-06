﻿using Kudu.Contracts.Settings;
using Kudu.Contracts.Tracing;
using Kudu.Core.Deployment.Generator;
using Kudu.Core.Infrastructure;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Kudu.Core.Deployment
{
    public class CustomBuilder : ISiteBuilder
    {
        private readonly string _command;
        private readonly string _repositoryPath;
        private readonly string _tempPath;
        private readonly string _homePath;
        private readonly string _scriptPath;
        private readonly IBuildPropertyProvider _propertyProvider;
        private readonly IDeploymentSettingsManager _settings;

        public CustomBuilder(string repositoryPath, string tempPath, string command, IBuildPropertyProvider propertyProvider, string homePath, string scriptPath, IDeploymentSettingsManager settings)
        {
            _repositoryPath = repositoryPath;
            _tempPath = tempPath;
            _command = command;
            _propertyProvider = propertyProvider;
            _homePath = homePath;
            _scriptPath = scriptPath;
            _settings = settings;
        }

        public Task Build(DeploymentContext context)
        {
            var tcs = new TaskCompletionSource<object>();

            ILogger customLogger = context.Logger.Log("Running custom deployment command...");

            // Creates an executable pointing to cmd and the working directory being
            // the repository root
            var exe = new Executable(StarterScriptPath, _repositoryPath, _settings.GetCommandIdleTimeout());
            exe.AddDeploymentSettingsAsEnvironmentVariables(_settings);
            exe.EnvironmentVariables[ExternalCommandBuilder.SourcePath] = _repositoryPath;
            exe.EnvironmentVariables[ExternalCommandBuilder.TargetPath] = context.OutputPath;
            exe.EnvironmentVariables[ExternalCommandBuilder.PreviousManifestPath] = (context.PreviousManifest != null) ? context.PreviousManifest.ManifestFilePath : String.Empty;
            exe.EnvironmentVariables[ExternalCommandBuilder.NextManifestPath] = context.ManifestWriter.ManifestFilePath;
            exe.EnvironmentVariables[ExternalCommandBuilder.MSBuildPath] = PathUtility.ResolveMSBuildPath();
            exe.EnvironmentVariables[ExternalCommandBuilder.KuduSyncCommandKey] = KuduSyncCommand;
            exe.EnvironmentVariables[ExternalCommandBuilder.SelectNodeVersionCommandKey] = SelectNodeVersionCommand;
            exe.EnvironmentVariables[ExternalCommandBuilder.NpmJsPathKey] = PathUtility.ResolveNpmJsPath();
            exe.EnvironmentVariables[WellKnownEnvironmentVariables.NuGetPackageRestoreKey] = "true";

            exe.SetHomePath(_homePath);

            // Create a directory for the script output temporary artifacts
            string buildTempPath = Path.Combine(_tempPath, Guid.NewGuid().ToString());
            FileSystemHelpers.EnsureDirectory(buildTempPath);
            exe.EnvironmentVariables[ExternalCommandBuilder.BuildTempPath] = buildTempPath;

            // Populate the enviornment with the build propeties
            foreach (var property in _propertyProvider.GetProperties())
            {
                exe.EnvironmentVariables[property.Key] = property.Value;
            }

            // Set the path so we can add more variables
            exe.EnvironmentVariables["PATH"] = System.Environment.GetEnvironmentVariable("PATH");

            // Add the msbuild path and git path to the %PATH% so more tools are available
            var toolsPaths = new[] {
                Path.GetDirectoryName(PathUtility.ResolveMSBuildPath()),
                Path.GetDirectoryName(PathUtility.ResolveGitPath())
            };

            exe.AddToPath(toolsPaths);

            try
            {
                exe.ExecuteWithProgressWriter(customLogger, context.Tracer, ExternalCommandBuilder.ShouldFilterOutMsBuildWarnings, _command, String.Empty);

                tcs.SetResult(null);
            }
            catch (CommandLineException ex)
            {
                context.Tracer.TraceError(ex);

                // HACK: Log an empty error to the global logger (post receive hook console output).
                // The reason we don't log the real exception is because the 'live output' running
                // msbuild has already been captured.
                context.GlobalLogger.LogError();

                // Add the output stream and the error stream to the log for better
                // debugging
                customLogger.Log(ex.Output, LogEntryType.Error);
                customLogger.Log(ex.Error, LogEntryType.Error);

                tcs.SetException(ex);
            }
            finally
            {
                // Clean the temp folder up
                FileSystemHelpers.DeleteDirectorySafe(buildTempPath);
            }

            return tcs.Task;
        }

        private string KuduSyncCommand
        {
            get
            {
                return Path.Combine(_scriptPath, "kudusync.cmd");
            }
        }

        private string SelectNodeVersionCommand
        {
            get
            {
                return "node \"" + Path.Combine(_scriptPath, "selectNodeVersion") + "\"";
            }
        }

        private string StarterScriptPath
        {
            get
            {
                return Path.Combine(_scriptPath, ExternalCommandBuilder.StarterScriptName);
            }
        }
    }
}
