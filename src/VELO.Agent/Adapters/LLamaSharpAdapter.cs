using System.IO;
using LLama;
using LLama.Common;
using LLama.Sampling;
using Microsoft.Extensions.Logging;
using VELO.Agent.Models;

namespace VELO.Agent.Adapters;

/// <summary>
/// Runs a GGUF model locally via LLamaSharp (llama.cpp wrapper).
/// Zero network calls — model file lives at modelPath on the user's disk.
///
/// Recommended models (4-bit quantized, fit in 6 GB VRAM or run on CPU):
///   - Mistral-7B-Instruct-v0.3.Q4_K_M.gguf  (~4.4 GB)
///   - Llama-3.2-3B-Instruct.Q4_K_M.gguf     (~2.0 GB)
///   - Phi-3.5-mini-instruct.Q4_K_M.gguf      (~2.2 GB)
/// </summary>
public class LLamaSharpAdapter : IAgentAdapter, IDisposable
{
    private readonly ILogger<LLamaSharpAdapter> _logger;
    private readonly string _modelPath;

    private LLamaWeights?  _weights;
    private LLamaContext?  _ctx;
    private bool           _loaded;

    private const int ContextSize  = 4096;
    private const int GpuLayers    = 35;    // 0 = CPU-only; 35 covers most 7B models on 6 GB VRAM
    private const int MaxNewTokens = 512;

    public bool   IsAvailable => _loaded;
    public string BackendName => "LLamaSharp (local)";

    public LLamaSharpAdapter(string modelPath, ILogger<LLamaSharpAdapter> logger)
    {
        _modelPath = modelPath;
        _logger    = logger;
    }

    /// <summary>
    /// Load the model. Call once on startup (takes 2-10 s depending on model size).
    /// Safe to call even if the model file doesn't exist — IsAvailable stays false.
    /// </summary>
    public async Task LoadAsync()
    {
        if (!File.Exists(_modelPath))
        {
            _logger.LogWarning("LLamaSharp: model not found at {Path}", _modelPath);
            return;
        }

        await Task.Run(() =>
        {
            try
            {
                var parameters = new ModelParams(_modelPath)
                {
                    ContextSize = ContextSize,
                    GpuLayerCount = GpuLayers,
                };

                _weights = LLamaWeights.LoadFromFile(parameters);
                _ctx     = _weights.CreateContext(parameters);
                _loaded  = true;
                _logger.LogInformation("LLamaSharp: model loaded from {Path}", _modelPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LLamaSharp: failed to load model from {Path}", _modelPath);
                _loaded = false;
            }
        });
    }

    public async Task<AgentResponse> ChatAsync(
        string userPrompt,
        AgentContext context,
        CancellationToken ct = default)
    {
        if (!_loaded || _ctx == null)
            return AgentResponse.Error("Modelo local no cargado. Verifica que el archivo GGUF existe.");

        try
        {
            var executor = new InstructExecutor(_ctx);

            var inferParams = new InferenceParams
            {
                MaxTokens        = MaxNewTokens,
                AntiPrompts      = ["\nUsuario:", "\nUser:"],
                SamplingPipeline = new DefaultSamplingPipeline
                {
                    Temperature = 0.3f,
                    TopP        = 0.9f,
                },
            };

            var fullPrompt = BuildPrompt(userPrompt, context);
            var sb         = new System.Text.StringBuilder();

            await foreach (var token in executor.InferAsync(fullPrompt, inferParams, ct))
                sb.Append(token);

            var raw = sb.ToString().Trim();
            _logger.LogDebug("LLamaSharp raw response: {Raw}", raw[..Math.Min(raw.Length, 200)]);

            return AgentResponseParser.Parse(raw);
        }
        catch (OperationCanceledException)
        {
            return AgentResponse.Error("Inferencia cancelada.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLamaSharp: inference error");
            return AgentResponse.Error($"Error de inferencia: {ex.Message}");
        }
    }

    private static string BuildPrompt(string userPrompt, AgentContext context)
    {
        // Instruct format compatible with Mistral, Llama-3, Phi-3
        var system  = AgentPromptBuilder.SystemPrompt;
        var user    = AgentPromptBuilder.BuildUserMessage(userPrompt, context);

        return $"[INST] <<SYS>>\n{system}\n<</SYS>>\n\n{user} [/INST]";
    }

    public void Dispose()
    {
        _ctx?.Dispose();
        _weights?.Dispose();
        _loaded = false;
    }
}
