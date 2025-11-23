// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

namespace GitClub.Tests
{
    public class DockerContainers
    {
        public static INetwork ServicesNetwork = new NetworkBuilder()
                .WithName("services")
                .WithDriver(NetworkDriver.Bridge)
                .Build();

        public static IContainer PostgresContainer = new ContainerBuilder()
            .WithName("postgres")
            .WithImage("postgres:16")
            .WithNetwork(ServicesNetwork)
            .WithPortBinding(hostPort: 5432, containerPort: 5432)
            // Mount Postgres Configuration and SQL Scripts 
            .WithBindMount(Path.Combine(Directory.GetCurrentDirectory(), "Resources/docker/postgres.conf"), "/usr/local/etc/postgres/postgres.conf")
            .WithBindMount(Path.Combine(Directory.GetCurrentDirectory(), "Resources/sql/absurd.sql"), "/docker-entrypoint-initdb.d/1-absurd.sql")
            // Set Username and Password
            .WithEnvironment(new Dictionary<string, string>
            {
                    {"POSTGRES_USER", "postgres" },
                    {"POSTGRES_PASSWORD", "password" },
            })
            // Start Postgres with the given postgres.conf.
            .WithCommand([
                "postgres",
                "-c",
                "config_file=/usr/local/etc/postgres/postgres.conf"
            ])
            // Wait until the Port is exposed.
            .WithWaitStrategy(Wait
                .ForUnixContainer()
                .UntilExternalTcpPortIsAvailable(5432))
            .Build();

        public static async Task StartAllContainersAsync()
        {
            await PostgresContainer.StartAsync();
        }

        public static async Task StopAllContainersAsync()
        {
            await PostgresContainer.StopAsync();
        }
    }
}
