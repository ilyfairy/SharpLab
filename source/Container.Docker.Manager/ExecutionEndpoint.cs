using System.Buffers;
using System.Buffers.Text;
using System.Formats.Tar;
using Docker.DotNet;
using Docker.DotNet.Models;
using ProtoBuf;
using SharpLab.Container.Manager.Internal;
using SharpLab.Container.Protocol.Stdin;

namespace SharpLab.Container.Docker.Manager;

public class ExecutionEndpoint {
    private readonly ILogger<ExecutionContext> _logger;
    private readonly ContainerOptions _containerOptions;

    private readonly DockerClient _docker;

    private readonly StdoutReader _stdoutReader;

    public ExecutionEndpoint(ILogger<ExecutionContext> logger, ILogger<StdoutReader> stdoutReaderLogger, ContainerOptions containerOptions) {
        _logger = logger;
        _containerOptions = containerOptions;

        if (string.IsNullOrWhiteSpace(containerOptions.DockerUnixSocketPath)) {
            _docker = new DockerClientConfiguration().CreateClient();
        }
        else {
            _docker = new DockerClientConfiguration(new Uri(containerOptions.DockerUnixSocketPath)).CreateClient();
        }

        _stdoutReader = new(stdoutReaderLogger);
    }

    public async Task Execute(HttpContext context) {
        var authorization = context.Request.Headers.Authorization;
        if (authorization.Count != 1 || authorization[0] != _containerOptions.ContainerHostAuthorizationToken) {
            context.Response.StatusCode = 401;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            return;
        }

        var sessionId = context.Request.Headers["SL-Session-Id"][0]!;
        var containerName = $"SharpLab_{sessionId}";
        var cancellationToken = context.RequestAborted;
        var contentLength = (int?)context.Request.Headers.ContentLength;
        var includePerformance = context.Request.Headers["SL-Debug-Performance"].Count > 0;

        try {
            _logger.LogInformation("create container {ContainerName}", containerName);
            var container = await _docker.Containers.CreateContainerAsync(new CreateContainerParameters() {
                Name = containerName,
                Image = _containerOptions.DockerImageName,
                HostConfig = new HostConfig() {
                    CPUCount = 1,
                    NanoCPUs = 500_000_000,
                    MemorySwappiness = 0,
                    Memory = 50 * 1024 * 1024,
                    OomKillDisable = false,
                    PidsLimit = 10,
                    NetworkMode = "none",
                    Privileged = false,
                },
                AttachStdin = true,
                AttachStdout = true,
                AttachStderr = true,
                OpenStdin = true,
                NetworkDisabled = true,
                Tty = false,
            }, cancellationToken);

            var stopCancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(_containerOptions.ContainerExecutionTimeout));
            cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopCancellationTokenSource.Token).Token;

            byte[]? bodyBuffer = null;
            byte[]? outputBuffer = null;
            ExecutionOutputResult result = default;
            try {
                bodyBuffer = ArrayPool<byte>.Shared.Rent(contentLength ?? 10240);
                bodyBuffer.AsSpan(contentLength ?? 0).Clear();
                outputBuffer = ArrayPool<byte>.Shared.Rent(10240);
                MemoryStream bodyMemoryStream = new(bodyBuffer, 0, bodyBuffer.Length, true, true);
                await context.Request.BodyReader.CopyToAsync(bodyMemoryStream);

                // start container
                result = await ExecuteInContainerAsync(containerName, bodyMemoryStream.GetBuffer(), outputBuffer, includePerformance, cancellationToken);
            }
            catch (OperationCanceledException) {
                result = ExecutionOutputResult.Failure(FailureMessages.TimedOut);
            }
            finally {
                if (bodyBuffer != null)
                    ArrayPool<byte>.Shared.Return(bodyBuffer);
                if (outputBuffer != null)
                    ArrayPool<byte>.Shared.Return(outputBuffer);
            }

            await _docker.Containers.StopContainerAsync(containerName, new ContainerStopParameters());

            try {
                context.Response.StatusCode = 200;
                if (!result.IsSuccess)
                    context.Response.Headers.Append("SL-Container-Output-Failed", "true");
                await context.Response.BodyWriter.WriteAsync(result.Output, context.RequestAborted);
                if (!result.IsSuccess)
                    await context.Response.BodyWriter.WriteAsync(result.FailureMessage, context.RequestAborted);
            }
            catch (Exception ex) {
                await context.Response.WriteAsync(ex.ToString(), context.RequestAborted);
            }
        }
        catch (Exception exception) {
            context.Response.StatusCode = 500;
            context.Response.ContentType = "text/vnd.sharplab.error+plain";
            _logger.LogError(exception, "Execution endpoint error");
            await context.Response.WriteAsync(exception.ToString(), context.RequestAborted);
            return;
        }
        finally {
            try {
                await _docker.Containers.RemoveContainerAsync(containerName, new ContainerRemoveParameters() {
                    Force = true,
                });
            }
            catch { }
        }
    }

    public async Task<ExecutionOutputResult> ExecuteInContainerAsync(
            string containerId,
            byte[] assemblyBytes,
            byte[] outputBufferBytes,
            bool includePerformance,
            CancellationToken cancellationToken
        ) {
        var outputStartMarker = Guid.NewGuid();
        var outputEndMarker = Guid.NewGuid();

        await _docker.Containers.StartContainerAsync(containerId, new(), cancellationToken);

        using var stream = new MultiplexedStreamReader(await _docker.Containers.AttachContainerAsync(containerId, false, new() {
            Stdin = true,
            Stderr = true,
            Stdout = true,
            Stream = true,
        }, cancellationToken));

        var executeCommand = new ExecuteCommand(
            assemblyBytes,
            outputStartMarker,
            outputEndMarker,
            includePerformance
        );

        try {
            Serializer.SerializeWithLengthPrefix(stream, executeCommand, PrefixStyle.Base128);
        }
        catch (IOException ex) {
            _logger.LogInformation(ex, "Failed to write stream");
            return ExecutionOutputResult.Failure(FailureMessages.IOFailure);
        }
        catch (OperationCanceledException) {
            _logger.LogDebug("Timed out while writing stream");
            return ExecutionOutputResult.Failure(FailureMessages.TimedOut);
        }

        const int OutputMarkerLength = 36; // length of guid
        byte[]? outputStartMarkerBytes = null;
        byte[]? outputEndMarkerBytes = null;
        try {
            outputStartMarkerBytes = ArrayPool<byte>.Shared.Rent(OutputMarkerLength);
            outputEndMarkerBytes = ArrayPool<byte>.Shared.Rent(OutputMarkerLength);

            Utf8Formatter.TryFormat(outputStartMarker, outputStartMarkerBytes, out _);
            Utf8Formatter.TryFormat(outputEndMarker, outputEndMarkerBytes, out _);

            return await _stdoutReader.ReadOutputAsync(
                stream,
                outputBufferBytes,
                outputStartMarkerBytes.AsMemory(0, OutputMarkerLength),
                outputEndMarkerBytes.AsMemory(0, OutputMarkerLength),
                cancellationToken
            );
        }
        finally {
            if (outputStartMarkerBytes != null)
                ArrayPool<byte>.Shared.Return(outputStartMarkerBytes);
            if (outputEndMarkerBytes != null)
                ArrayPool<byte>.Shared.Return(outputEndMarkerBytes);
        }
    }

    private async Task WriteFileToContainer(string containerId, Stream stream, string dir, string fileName, CancellationToken cancellationToken) {
        MemoryStream tarStream = new();
        var tar = new TarWriter(tarStream, true);
        tar.WriteEntry(new GnuTarEntry(TarEntryType.RegularFile, fileName) {
            DataStream = stream,
        });
        tar.Dispose();
        tarStream.Position = 0;
        await _docker.Containers.ExtractArchiveToContainerAsync(containerId, new ContainerPathStatParameters() {
            Path = dir,
            AllowOverwriteDirWithFile = true,
        }, tarStream, cancellationToken);
    }
}

file class MultiplexedStreamReader(MultiplexedStream multiplexedStream) : Stream {
    public override bool CanRead => true;
    public override bool CanSeek { get; }
    public override bool CanWrite => true;
    public override long Length { get; }
    public override long Position { get; set; }

    public override void Flush() {
    }

    public override int Read(byte[] buffer, int offset, int count) {
        return ReadAsync(buffer, offset, count).Result;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
        var result = await multiplexedStream.ReadOutputAsync(buffer, offset, count, cancellationToken);
        return result.Count;
    }

    public override long Seek(long offset, SeekOrigin origin) {
        throw new NotSupportedException();
    }

    public override void SetLength(long value) {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) {
        WriteAsync(buffer, offset, count).Wait();
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
        return multiplexedStream.WriteAsync(buffer, offset, count, cancellationToken);
    }

    protected override void Dispose(bool disposing) {
        multiplexedStream.Dispose();
    }
}