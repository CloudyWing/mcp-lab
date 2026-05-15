namespace CloudyWing.McpLab.Docker;

/// <summary>
/// Provides read-only MCP tools for inspecting Docker and Compose state.
/// </summary>
[McpServerToolType]
public sealed class DockerTools {
    private const int DefaultListLimit = 100;
    private const int MaxListLimit = 500;
    private const int DefaultLogTail = 100;
    private const int MaxLogTail = 500;
    private readonly DockerEngineClient client;

    /// <summary>
    /// Initializes a new instance of <see cref="DockerTools"/> with the Docker Engine client.
    /// </summary>
    public DockerTools(DockerEngineClient client) {
        this.client = client;
    }

    /// <summary>
    /// Checks whether Docker Engine is reachable.
    /// </summary>
    [McpServerTool, Description("檢查 Docker Engine 是否可連線")]
    public async Task<string> PingDocker() {
        try {
            string response = (await client.GetStringAsync("/_ping").ConfigureAwait(false)).Trim();

            return response == "OK"
                ? ToolResponse.Ok(new { status = response })
                : ToolResponse.Error($"Unexpected Docker ping response: {response}");
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// Gets Docker Engine version details.
    /// </summary>
    [McpServerTool, Description("取得 Docker Engine 版本資訊")]
    public async Task<string> GetDockerVersion() {
        try {
            JsonObject version = await client.GetObjectAsync("/version").ConfigureAwait(false);

            return ToolResponse.Ok(version);
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// Lists Compose projects found from container labels.
    /// </summary>
    [McpServerTool, Description("依容器 label 列出 Docker Compose project 摘要")]
    public async Task<string> ListComposeProjects() {
        try {
            JsonArray containers = await client.GetArrayAsync("/containers/json?all=1").ConfigureAwait(false);
            Dictionary<string, ProjectSummary> projects = new(StringComparer.OrdinalIgnoreCase);

            foreach (JsonNode? container in containers) {
                string project = GetLabel(container, "com.docker.compose.project");

                if (string.IsNullOrEmpty(project)) {
                    continue;
                }

                if (!projects.TryGetValue(project, out ProjectSummary? summary)) {
                    summary = new ProjectSummary(project);
                    projects[project] = summary;
                }

                summary.Add(
                    GetLabel(container, "com.docker.compose.service"),
                    GetString(container, "State")
                );
            }

            object[] output = projects.Values
                .OrderBy(p => p.Project, StringComparer.OrdinalIgnoreCase)
                .Select(p => p.ToResponse())
                .ToArray();

            return output.Length > 0
                ? ToolResponse.Ok(output)
                : ToolResponse.Empty("No Docker Compose projects found.", output);
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// Lists containers with optional Compose project and service filters.
    /// </summary>
    [McpServerTool, Description("列出容器，可依 Compose project 或 service label 篩選")]
    public async Task<string> ListContainers(
        [Description("是否包含已停止容器")] bool all = false,
        [Description("Compose project 名稱，空字串表示不篩選")] string composeProject = "",
        [Description("Compose service 名稱，空字串表示不篩選")] string service = "",
        [Description("最大回傳容器數，預設 100，上限 500")] int limit = 0
    ) {
        try {
            int safeLimit = ToolRuntimeOptions.NormalizeRequestedInt32(
                limit,
                DefaultListLimit,
                1,
                MaxListLimit
            );
            JsonArray containers = await client.GetArrayAsync($"/containers/json?all={(all ? "1" : "0")}")
                .ConfigureAwait(false);
            List<object> output = [];

            foreach (JsonNode? container in containers) {
                if (!MatchesFilter(container, composeProject, service)) {
                    continue;
                }

                output.Add(ToContainerSummary(container));

                if (output.Count >= safeLimit) {
                    break;
                }
            }

            object data = new {
                returned_count = output.Count,
                limit = safeLimit,
                containers = output,
            };

            return output.Count > 0
                ? ToolResponse.Ok(data)
                : ToolResponse.Empty("No containers found.", data);
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// Inspects a container without returning environment variables.
    /// </summary>
    [McpServerTool, Description("檢視指定容器狀態，不回傳環境變數")]
    public async Task<string> InspectContainer(
        [Description("容器名稱或 ID")] string container
    ) {
        try {
            JsonObject root = await client.GetObjectAsync($"/containers/{EscapeSegment(container)}/json")
                .ConfigureAwait(false);

            return ToolResponse.Ok(new {
                id = ShortId(GetString(root, "Id")),
                full_id = GetString(root, "Id"),
                name = GetString(root, "Name").TrimStart('/'),
                image = GetString(root, "Config", "Image"),
                created = GetString(root, "Created"),
                state = new {
                    status = GetString(root, "State", "Status"),
                    running = GetBool(root, "State", "Running"),
                    paused = GetBool(root, "State", "Paused"),
                    restarting = GetBool(root, "State", "Restarting"),
                    oom_killed = GetBool(root, "State", "OOMKilled"),
                    dead = GetBool(root, "State", "Dead"),
                    pid = GetNumber(root, "State", "Pid"),
                    exit_code = GetNumber(root, "State", "ExitCode"),
                    error = GetString(root, "State", "Error"),
                    started_at = GetString(root, "State", "StartedAt"),
                    finished_at = GetString(root, "State", "FinishedAt"),
                    health = GetString(root, "State", "Health", "Status"),
                },
                compose = GetComposeLabels(root["Config"]?["Labels"]),
                ports = GetPorts(root["NetworkSettings"]?["Ports"]),
                networks = GetNetworks(root["NetworkSettings"]?["Networks"]),
                mounts = GetMounts(root["Mounts"]),
            });
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// Gets recent stdout/stderr logs for a container.
    /// </summary>
    [McpServerTool, Description("取得容器最近 stdout/stderr logs，僅支援 tail 並限制最大行數")]
    public async Task<string> GetContainerLogs(
        [Description("容器名稱或 ID")] string container,
        [Description("回傳最後幾行，預設 100，上限 500")] int tail = 0,
        [Description("是否包含 Docker timestamp")] bool timestamps = true
    ) {
        try {
            int safeTail = ToolRuntimeOptions.NormalizeRequestedInt32(tail, DefaultLogTail, 1, MaxLogTail);
            string path = $"/containers/{EscapeSegment(container)}/logs?stdout=true&stderr=true"
                + $"&timestamps={timestamps.ToString().ToLowerInvariant()}&tail={safeTail}";
            byte[] bytes = await client.GetBytesAsync(path).ConfigureAwait(false);
            string logs = DockerLogStream.Decode(bytes);
            object data = new {
                container,
                tail = safeTail,
                logs,
            };

            return string.IsNullOrEmpty(logs)
                ? ToolResponse.Empty("No logs returned.", data)
                : ToolResponse.Ok(data);
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// Gets a single non-streaming container stats sample.
    /// </summary>
    [McpServerTool, Description("取得單一容器的 CPU、記憶體、網路與 block I/O 統計快照")]
    public async Task<string> GetContainerStats(
        [Description("容器名稱或 ID")] string container
    ) {
        try {
            JsonObject stats = await client.GetObjectAsync($"/containers/{EscapeSegment(container)}/stats?stream=false")
                .ConfigureAwait(false);

            return ToolResponse.Ok(new {
                container,
                cpu = GetCpuStats(stats),
                memory = GetMemoryStats(stats),
                networks = GetNetworkStats(stats["networks"]),
                block_io = GetBlockIoStats(stats["blkio_stats"]?["io_service_bytes_recursive"]),
                pids_current = GetNumber(stats, "pids_stats", "current"),
            });
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    private static bool MatchesFilter(JsonNode? container, string composeProject, string service) {
        if (!string.IsNullOrWhiteSpace(composeProject)
            && !string.Equals(
                GetLabel(container, "com.docker.compose.project"),
                composeProject,
                StringComparison.OrdinalIgnoreCase
            )) {
            return false;
        }

        return string.IsNullOrWhiteSpace(service)
            || string.Equals(
                GetLabel(container, "com.docker.compose.service"),
                service,
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static object ToContainerSummary(JsonNode? container) {
        string id = GetString(container, "Id");

        return new {
            id = ShortId(id),
            full_id = id,
            names = GetStringArray(container?["Names"]),
            image = GetString(container, "Image"),
            image_id = ShortId(GetString(container, "ImageID")),
            command = GetString(container, "Command"),
            created = GetNumber(container, "Created"),
            state = GetString(container, "State"),
            status = GetString(container, "Status"),
            compose = GetComposeLabels(container?["Labels"]),
            ports = GetContainerListPorts(container?["Ports"]),
        };
    }

    private static object GetCpuStats(JsonNode? stats) {
        double cpuDelta = GetNumber(stats, "cpu_stats", "cpu_usage", "total_usage")
            - GetNumber(stats, "precpu_stats", "cpu_usage", "total_usage");
        double systemDelta = GetNumber(stats, "cpu_stats", "system_cpu_usage")
            - GetNumber(stats, "precpu_stats", "system_cpu_usage");
        double onlineCpus = GetNumber(stats, "cpu_stats", "online_cpus");

        if (onlineCpus <= 0) {
            onlineCpus = stats?["cpu_stats"]?["cpu_usage"]?["percpu_usage"]?.AsArray().Count ?? 0;
        }

        double percent = cpuDelta > 0 && systemDelta > 0 && onlineCpus > 0
            ? cpuDelta / systemDelta * onlineCpus * 100
            : 0;

        return new {
            percent,
            online_cpus = onlineCpus,
        };
    }

    private static object GetMemoryStats(JsonNode? stats) {
        double usage = GetNumber(stats, "memory_stats", "usage");
        double limit = GetNumber(stats, "memory_stats", "limit");

        return new {
            usage_bytes = usage,
            limit_bytes = limit,
            percent = limit > 0 ? usage / limit * 100 : 0,
        };
    }

    private static object[] GetNetworkStats(JsonNode? networks) {
        if (networks is not JsonObject obj) {
            return [];
        }

        return obj.Select(kv => new {
            name = kv.Key,
            rx_bytes = GetNumber(kv.Value, "rx_bytes"),
            tx_bytes = GetNumber(kv.Value, "tx_bytes"),
            rx_packets = GetNumber(kv.Value, "rx_packets"),
            tx_packets = GetNumber(kv.Value, "tx_packets"),
        }).ToArray();
    }

    private static object GetBlockIoStats(JsonNode? ioStats) {
        double read = 0;
        double write = 0;

        if (ioStats is JsonArray arr) {
            foreach (JsonNode? item in arr) {
                string op = GetString(item, "op");
                double value = GetNumber(item, "value");

                if (string.Equals(op, "Read", StringComparison.OrdinalIgnoreCase)) {
                    read += value;
                } else if (string.Equals(op, "Write", StringComparison.OrdinalIgnoreCase)) {
                    write += value;
                }
            }
        }

        return new {
            read_bytes = read,
            write_bytes = write,
        };
    }

    private static object GetComposeLabels(JsonNode? labels) => new {
        project = GetLabel(labels, "com.docker.compose.project"),
        service = GetLabel(labels, "com.docker.compose.service"),
        container_number = GetLabel(labels, "com.docker.compose.container-number"),
        working_dir = GetLabel(labels, "com.docker.compose.project.working_dir"),
        config_files = GetLabel(labels, "com.docker.compose.project.config_files"),
    };

    private static object[] GetPorts(JsonNode? ports) {
        if (ports is not JsonObject obj) {
            return [];
        }

        List<object> output = [];

        foreach ((string containerPort, JsonNode? bindings) in obj) {
            if (bindings is not JsonArray arr || arr.Count == 0) {
                output.Add(new {
                    container = containerPort,
                    host_ip = "",
                    host_port = "",
                });
                continue;
            }

            foreach (JsonNode? binding in arr) {
                output.Add(new {
                    container = containerPort,
                    host_ip = GetString(binding, "HostIp"),
                    host_port = GetString(binding, "HostPort"),
                });
            }
        }

        return output.ToArray();
    }

    private static object[] GetContainerListPorts(JsonNode? ports) {
        if (ports is not JsonArray arr) {
            return [];
        }

        return arr.Select(port => new {
            ip = GetString(port, "IP"),
            private_port = GetNumber(port, "PrivatePort"),
            public_port = GetNumber(port, "PublicPort"),
            type = GetString(port, "Type"),
        }).ToArray();
    }

    private static object[] GetNetworks(JsonNode? networks) {
        if (networks is not JsonObject obj) {
            return [];
        }

        return obj.Select(kv => new {
            name = kv.Key,
            network_id = ShortId(GetString(kv.Value, "NetworkID")),
            endpoint_id = ShortId(GetString(kv.Value, "EndpointID")),
            ip_address = GetString(kv.Value, "IPAddress"),
            gateway = GetString(kv.Value, "Gateway"),
        }).ToArray();
    }

    private static object[] GetMounts(JsonNode? mounts) {
        if (mounts is not JsonArray arr) {
            return [];
        }

        return arr.Select(mount => new {
            type = GetString(mount, "Type"),
            name = GetString(mount, "Name"),
            source = GetString(mount, "Source"),
            destination = GetString(mount, "Destination"),
            rw = GetBool(mount, "RW"),
        }).ToArray();
    }

    private static string[] GetStringArray(JsonNode? node) {
        if (node is not JsonArray arr) {
            return [];
        }

        return arr.Select(item => item?.ToString() ?? "").ToArray();
    }

    private static string GetLabel(JsonNode? node, string key) {
        JsonNode? labels = node?["Labels"] ?? node;

        return labels?[key]?.ToString() ?? "";
    }

    private static string GetString(JsonNode? node, params string[] path) {
        JsonNode? current = GetNode(node, path);

        return current?.ToString() ?? "";
    }

    private static bool GetBool(JsonNode? node, params string[] path) {
        JsonNode? current = GetNode(node, path);

        return current is not null && bool.TryParse(current.ToString(), out bool value) && value;
    }

    private static double GetNumber(JsonNode? node, params string[] path) {
        JsonNode? current = GetNode(node, path);

        return current is not null && double.TryParse(current.ToString(), out double value) ? value : 0;
    }

    private static JsonNode? GetNode(JsonNode? node, params string[] path) {
        JsonNode? current = node;

        foreach (string segment in path) {
            current = current?[segment];
        }

        return current;
    }

    private static string ShortId(string id) =>
        id.Length > 12 ? id[..12] : id;

    private static string EscapeSegment(string value) =>
        Uri.EscapeDataString(value.Trim().TrimStart('/'));

    private sealed class ProjectSummary {
        private readonly Dictionary<string, int> states = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> services = new(StringComparer.OrdinalIgnoreCase);

        public ProjectSummary(string project) {
            Project = project;
        }

        public string Project { get; }

        public int Total { get; private set; }

        public void Add(string service, string state) {
            Total++;

            if (!string.IsNullOrWhiteSpace(service)) {
                services.Add(service);
            }

            string stateKey = string.IsNullOrWhiteSpace(state) ? "unknown" : state;
            states[stateKey] = states.GetValueOrDefault(stateKey) + 1;
        }

        public object ToResponse() => new {
            project = Project,
            containers = Total,
            states,
            services = services.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray(),
        };
    }
}
