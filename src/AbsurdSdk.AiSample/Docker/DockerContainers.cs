// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using DotNet.Testcontainers;
using DotNet.Testcontainers.Builders;
using Testcontainers.PostgreSql;

namespace AbsurdSdk.AiSample.Docker
{
    public class DockerContainers
    {
        public static PostgreSqlContainer PostgresContainer = new PostgreSqlBuilder("postgres:18")
            .WithName("postgres")
            .WithEnvironment("POSTGRES_CONFIG_FILE", "/usr/local/etc/postgres/postgres.conf")
            .WithEnvironment("PGDATA", "/var/lib/postgresql/data/pgdata")
            .WithEnvironment("POSTGRES_USER", "postgres")
            .WithEnvironment("POSTGRES_PASSWORD", "password")
            .WithEnvironment("POSTGRES_DB", "abdurd_db")
            // Mount Postgres Configuration and SQL Scripts 
            .WithBindMount(Path.Combine(AppContext.BaseDirectory, "Resources\\docker\\postgres.conf"), "/usr/local/etc/postgres/postgres.conf")
            .WithBindMount(Path.Combine(AppContext.BaseDirectory, "Resources\\sql\\absurd.sql"), "/docker-entrypoint-initdb.d/1-absurd.sql")
            // Port Configuration
            .WithPortBinding(5432, 5432)
            // Wait until the Port is exposed.
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilMessageIsLogged("database system is ready to accept connections"))
            .WithLogger(ConsoleLogger.Instance)
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
