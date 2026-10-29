using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace HalMenu.Core
{
    /// <summary>
    /// Windows-only pre-menu. It invokes the ROS repository's PowerShell launcher,
    /// streams its progress and opens the regular menu only after ROS TCP is ready.
    /// No credential or SDK-specific Noraxon setting is stored in the Unity project.
    /// </summary>
    public class SystemLauncherController : MonoBehaviour
    {
        private const string StatusPrefix = "##REHAB_STATUS##|";
        private readonly object _outputLock = new object();
        private readonly List<string> _pendingOutput = new List<string>();
        private readonly List<string> _recentOutput = new List<string>();

        private Action _onReady;
        private Process _process;
        private bool _started;
        private bool _finished;
        private bool _success;
        private bool _guidao = true;
        private bool _pulmonary;
        private bool _emotiv;
        private bool _noraxon;
        private bool _rebuild;
        private string _status = "Selecione os dispositivos que serao usados nesta sessao.";
        private float _progress;

        public void Initialize(Action onReady)
        {
            _onReady = onReady;
        }

        private void Update()
        {
            lock (_outputLock)
            {
                foreach (string line in _pendingOutput)
                    ConsumeOutput(line);
                _pendingOutput.Clear();
            }

            if (_finished && _success)
            {
                _finished = false;
                enabled = false;
                _onReady?.Invoke();
            }
        }

        private void OnGUI()
        {
            GUIStyle title = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            GUIStyle text = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            GUIStyle status = new GUIStyle(text) { alignment = TextAnchor.MiddleCenter };
            GUIStyle log = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, normal = { textColor = new Color(0.75f, 0.78f, 0.82f) } };

            float width = Mathf.Min(760, Screen.width - 40);
            float height = Mathf.Min(650, Screen.height - 40);
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

            GUILayout.Space(12);
            if (!_started)
            {
                GUILayout.Label("Os USBs selecionados sao identificados automaticamente. Na primeira vez, o Windows pode pedir permissao para compartilha-los com o WSL2. O Docker sera reutilizado se a imagem ja existir.", text);
                GUILayout.Space(10);
                if (GUILayout.Button("INICIAR SISTEMA", GUILayout.Height(44))) StartSystem();
            }
            else
            {
                GUILayout.Label(_status, status, GUILayout.Height(36));
                Rect progressRect = GUILayoutUtility.GetRect(1, 22, GUILayout.ExpandWidth(true));
                GUI.Box(progressRect, GUIContent.none);
                GUI.Box(new Rect(progressRect.x, progressRect.y, progressRect.width * _progress, progressRect.height), GUIContent.none);
                GUILayout.Space(8);
                GUILayout.Label("Diagnostico recente", text);
                foreach (string line in SnapshotOutput()) GUILayout.Label(line, log);
                if (_finished && !_success)
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
                _status = "Nao encontrei Start-RehabSystem.ps1. Defina REHAB_PLATFORM_ROOT para a pasta do repositorio ROS.";
                return;
            }

            var selected = new List<string>();
            if (_guidao) selected.Add("guidao");
            if (_pulmonary) selected.Add("pulmonary");
            if (_emotiv) selected.Add("emotiv");
            if (_noraxon) selected.Add("noraxon");

            string arguments = "-NoProfile -ExecutionPolicy Bypass -File " + Quote(scriptPath) +
                               " -Devices " + string.Join(",", selected.ToArray());
            if (_rebuild) arguments += " -Rebuild";

            try
            {
                _status = "Iniciando...";
                _progress = 0;
                _started = true;
                _finished = false;
                _success = false;
                var info = new ProcessStartInfo("powershell.exe", arguments)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(scriptPath)
                };
                _process = new Process { StartInfo = info, EnableRaisingEvents = true };
                _process.OutputDataReceived += (_, e) => QueueOutput(e.Data);
                _process.ErrorDataReceived += (_, e) => QueueOutput(e.Data);
                _process.Exited += (_, __) => { _success = _process.ExitCode == 0; _finished = true; };
                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }
            catch (Exception exception)
            {
                _status = "Nao foi possivel abrir o PowerShell: " + exception.Message;
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
            _recentOutput.Add(line);
            if (_recentOutput.Count > 12) _recentOutput.RemoveAt(0);
            if (!line.StartsWith(StatusPrefix, StringComparison.Ordinal)) return;
            string[] parts = line.Substring(StatusPrefix.Length).Split(new[] { '|' }, 2);
            if (parts.Length == 2 && float.TryParse(parts[0], out float percentage))
            {
                _progress = Mathf.Clamp01(percentage / 100f);
                _status = parts[1];
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
