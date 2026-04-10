// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Networks;
using Testcontainers.PostgreSql;

namespace AbsurdSdk.Tests
{
    public static class DockerContainers
    {
        public static INetwork ServicesNetwork = new NetworkBuilder()
                .WithName("services")
                .WithDriver(NetworkDriver.Bridge)
                .Build();

        public static PostgreSqlContainer PostgresContainer = new PostgreSqlBuilder()
            .WithName("postgres")
            .WithImage("postgres:18")
            .WithNetwork(ServicesNetwork)
            // Mount Postgres Configuration and SQL Scripts 
            .WithBindMount(Path.Combine(Directory.GetCurrentDirectory(), "Resources/docker/postgres.conf"), "/usr/local/etc/postgres/postgres.conf")
            .WithBindMount(Path.Combine(Directory.GetCurrentDirectory(), "Resources/sql/absurd.sql"), "/docker-entrypoint-initdb.d/1-absurd.sql")
            // Set Username and Password
            .WithUsername("postgres")
            .WithPassword("password")
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
