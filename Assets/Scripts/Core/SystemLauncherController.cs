using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace HalMenu.Core
{
    /// <summary>
    /// Windows-only pre-menu. It invokes the ROS repository's PowerShell launcher,
    /// streams its progress and opens the regular menu only after the script
    /// reports that ROS (and, when selected, the Noraxon EMG stream) is ready.
    /// No credential or SDK-specific Noraxon setting is stored in the Unity project.
    /// </summary>
    public class SystemLauncherController : MonoBehaviour
    {
        private const string StatusPrefix = "##REHAB_STATUS##|";
        private const string ErrorPrefix = "##REHAB_ERROR##|";
        private const int MaxDiagnosticLines = 12;

        // Stop every container and the Noraxon sender when the app closes
        // (also when leaving Play mode in the Editor). Without this the stack
        // keeps running and, because of `restart: unless-stopped`, even comes
        // back by itself after a reboot. Startup cleans up either way.
        private const bool ShutdownPlatformOnQuit = true;

        private readonly object _outputLock = new object();
        private readonly List<string> _pendingOutput = new List<string>();
        private readonly List<string> _recentOutput = new List<string>();

        private Action _onReady;
        private Process _process;
        private bool _started;
        private volatile bool _finished;
        private volatile bool _success;
        private bool _failed;
        private bool _guidao = true;
        private bool _pulmonary;
        private bool _emotiv;
        private bool _noraxon;
        private bool _rebuild;
        private string _status = "Selecione os dispositivos que serao usados nesta sessao.";
        private float _progress;
        private float _nextForegroundGrant;
        private string _scriptPath;
        private bool _launchedOnce;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private const int AsfwAny = -1;

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);
#endif

        public void Initialize(Action onReady)
        {
            _onReady = onReady;
            // A static event, so it fires even after this component disables
            // itself when handing over to the menu.
            Application.quitting += OnApplicationQuitting;
        }

        private void OnDestroy()
        {
            Application.quitting -= OnApplicationQuitting;
        }

        private void OnApplicationQuitting()
        {
            if (!ShutdownPlatformOnQuit || !_launchedOnce || string.IsNullOrEmpty(_scriptPath)) return;
            try
            {
                // A startup still in progress must not bring containers up
                // again after the shutdown has removed them.
                if (_process != null && !_process.HasExited) _process.Kill();
            }
            catch (Exception) { }

            try
            {
                // Fire and forget: the shutdown script outlives the application.
                var info = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -File " + Quote(_scriptPath) + " -Shutdown")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(_scriptPath)
                };
                Process.Start(info);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning("Nao foi possivel encerrar a plataforma ROS: " + exception.Message);
            }
        }

        private void Update()
        {
            lock (_outputLock)
            {
                foreach (string line in _pendingOutput)
                    ConsumeOutput(line);
                _pendingOutput.Clear();
            }

            if (_started && !_finished && _noraxon)
                GrantForegroundToNoraxonSetup();

            if (_finished && _success)
            {
                _finished = false;
                enabled = false;
                _onReady?.Invoke();
            }
            else if (_finished && !_success && !_failed)
            {
                _failed = true;
                if (_process != null && !_status.StartsWith("Falha", StringComparison.Ordinal))
                    _status = "Falha ao iniciar o sistema. Veja o diagnostico abaixo.";
            }
        }

        /// <summary>
        /// With --use-setup the Noraxon SDK opens a modal hardware setup dialog
        /// from a background process. Windows' foreground lock would keep it
        /// behind this (foreground) window, so the operator never sees it.
        /// While Unity is in the foreground it may allow any process to take the
        /// foreground; the grant is renewed because user input resets it.
        /// </summary>
        private void GrantForegroundToNoraxonSetup()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (Time.unscaledTime < _nextForegroundGrant) return;
            _nextForegroundGrant = Time.unscaledTime + 1f;
            AllowSetForegroundWindow(AsfwAny);
#endif
        }

        private void OnGUI()
        {
            GUIStyle title = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            GUIStyle text = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            GUIStyle hint = new GUIStyle(text) { fontSize = 13, normal = { textColor = new Color(0.95f, 0.8f, 0.45f) } };
            GUIStyle status = new GUIStyle(text) { alignment = TextAnchor.MiddleCenter };
            if (_failed) status.normal.textColor = new Color(1f, 0.45f, 0.4f);
            GUIStyle log = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, normal = { textColor = new Color(0.75f, 0.78f, 0.82f) } };

            float width = Mathf.Min(760, Screen.width - 40);
            float height = Mathf.Min(680, Screen.height - 40);
            Rect panel = new Rect((Screen.width - width) / 2, (Screen.height - height) / 2, width, height);
            GUI.Box(panel, GUIContent.none);
            GUILayout.BeginArea(new Rect(panel.x + 30, panel.y + 25, panel.width - 60, panel.height - 50));
            GUILayout.Label("Preparar sessao de reabilitacao", title);
            GUILayout.Space(12);

            GUI.enabled = !_started;
            GUILayout.Label("Dispositivos da sessao", text);
            _guidao = GUILayout.Toggle(_guidao, " Guidão: encoder e controlador", GUILayout.Height(25));
            _pulmonary = GUILayout.Toggle(_pulmonary, " Sensor pulmonar", GUILayout.Height(25));
            _emotiv = GUILayout.Toggle(_emotiv, " EEG Emotiv (Cortex/Launcher aberto)", GUILayout.Height(25));
            _noraxon = GUILayout.Toggle(_noraxon, " EMG Noraxon (Acquire aberto)", GUILayout.Height(25));
            _rebuild = GUILayout.Toggle(_rebuild, " Reconstruir imagem Docker agora", GUILayout.Height(25));
            GUI.enabled = true;

            if (_noraxon)
                GUILayout.Label("Noraxon: durante a inicializacao pode abrir a janela de configuracao de hardware do Acquire. Confirme-a para continuar.", hint);

            GUILayout.Space(12);
            if (!_started)
            {
                GUILayout.Label("Os USBs selecionados sao identificados automaticamente. Na primeira vez, o Windows pode pedir permissao para compartilha-los com o WSL2. O Docker sera reutilizado se a imagem ja existir.", text);
                GUILayout.Space(10);
                if (GUILayout.Button("INICIAR SISTEMA", GUILayout.Height(44))) StartSystem();
            }
            else
            {
                GUILayout.Label(_status, status, GUILayout.MinHeight(36));
                Rect progressRect = GUILayoutUtility.GetRect(1, 22, GUILayout.ExpandWidth(true));
                GUI.Box(progressRect, GUIContent.none);
                GUI.Box(new Rect(progressRect.x, progressRect.y, progressRect.width * _progress, progressRect.height), GUIContent.none);
                GUILayout.Space(8);
                GUILayout.Label("Diagnostico recente", text);
                foreach (string line in SnapshotOutput()) GUILayout.Label(line, log);
                if (_failed)
                {
                    GUILayout.Space(8);
                    if (GUILayout.Button("TENTAR NOVAMENTE", GUILayout.Height(36))) ResetForRetry();
                }
            }
            GUILayout.EndArea();
        }

        private void StartSystem()
        {
            string scriptPath = FindLauncherScript();
            if (string.IsNullOrEmpty(scriptPath))
            {
                _started = true;
                _finished = true;
                _success = false;
                _status = "Falha: nao encontrei Start-RehabSystem.ps1. Defina REHAB_PLATFORM_ROOT para a pasta do repositorio ROS.";
                return;
            }

            var selected = new List<string>();
            if (_guidao) selected.Add("guidao");
            if (_pulmonary) selected.Add("pulmonary");
            if (_emotiv) selected.Add("emotiv");
            if (_noraxon) selected.Add("noraxon");

            _scriptPath = scriptPath;
            string arguments = "-NoProfile -ExecutionPolicy Bypass -File " + Quote(scriptPath);
            // An empty "-Devices" would be a PowerShell parameter binding error.
            if (selected.Count > 0) arguments += " -Devices " + string.Join(",", selected.ToArray());
            if (_rebuild) arguments += " -Rebuild";

            try
            {
                _status = "Iniciando...";
                _progress = 0;
                _started = true;
                _finished = false;
                _success = false;
                _failed = false;
                _nextForegroundGrant = 0f;
                lock (_outputLock) _recentOutput.Clear();

                var info = new ProcessStartInfo("powershell.exe", arguments)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(scriptPath)
                };
                _process = new Process { StartInfo = info, EnableRaisingEvents = true };
                _process.OutputDataReceived += (_, e) => QueueOutput(e.Data);
                _process.ErrorDataReceived += (_, e) => QueueOutput(e.Data);
                _process.Exited += (_, __) =>
                {
                    bool ok;
                    try { ok = _process.ExitCode == 0; } catch { ok = false; }
                    _success = ok;
                    _finished = true;
                };
                _process.Start();
                _launchedOnce = true;
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }
            catch (Exception exception)
            {
                _status = "Falha: nao foi possivel abrir o PowerShell: " + exception.Message;
                _finished = true;
                _success = false;
            }
        }

        private void QueueOutput(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (_outputLock)
            {
                _pendingOutput.Add(line.Trim());
            }
        }

        private void ConsumeOutput(string line)
        {
            if (line.StartsWith(ErrorPrefix, StringComparison.Ordinal))
            {
                _status = "Falha: " + line.Substring(ErrorPrefix.Length);
                return;
            }

            if (line.StartsWith(StatusPrefix, StringComparison.Ordinal))
            {
                string[] parts = line.Substring(StatusPrefix.Length).Split(new[] { '|' }, 2);
                if (parts.Length == 2 && float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float percentage))
                {
                    _progress = Mathf.Clamp01(percentage / 100f);
                    _status = parts[1];
                }
                return;
            }

            AddDiagnostic(line);
        }

        private void AddDiagnostic(string line)
        {
            lock (_outputLock)
            {
                _recentOutput.Add(line);
                if (_recentOutput.Count > MaxDiagnosticLines) _recentOutput.RemoveAt(0);
            }
        }

        private string[] SnapshotOutput()
        {
            lock (_outputLock) return _recentOutput.ToArray();
        }

        private void ResetForRetry()
        {
            _started = false;
            _finished = false;
            _success = false;
            _failed = false;
            _progress = 0;
            _status = "Ajuste a configuracao e tente novamente.";
        }

        private static string FindLauncherScript()
        {
            string root = Environment.GetEnvironmentVariable("REHAB_PLATFORM_ROOT");
            if (!string.IsNullOrWhiteSpace(root))
            {
                string fromEnvironment = Path.Combine(root, "scripts", "Start-RehabSystem.ps1");
                if (File.Exists(fromEnvironment)) return fromEnvironment;
            }
            // Editor builds and builds placed beneath the Unity repository can
            // locate the sibling ROS repository without any machine setting.
            DirectoryInfo directory = Directory.GetParent(Application.dataPath);
            for (int depth = 0; directory != null && depth < 5; depth++, directory = directory.Parent)
            {
                string sibling = Path.Combine(directory.FullName, "TCC-Gamified-Rehab-Plataform", "scripts", "Start-RehabSystem.ps1");
                if (File.Exists(sibling)) return sibling;
            }
            return null;
        }

        private static string Quote(string value) => "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
    }
}