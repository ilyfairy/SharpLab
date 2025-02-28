namespace SharpLab.Container.Docker.Manager;

public class ContainerOptions {
    public string? DockerUnixSocketPath { get; set; }
    public string DockerImageName { get; set; } = null!;
    public string ContainerHostAuthorizationToken { get; set; } = null!;
    public int ContainerExecutionTimeout { get; set; } = 10000; // ms
}
