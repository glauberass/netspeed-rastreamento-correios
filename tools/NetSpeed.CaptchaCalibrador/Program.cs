using System.Diagnostics;
using System.Net;
using System.Text;
using NetSpeed.Captcha;
using NetSpeed.Domain.Portas;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

// Ferramenta de calibração do CAPTCHA. Modos:
//   live <n> [pastaAmostras] [solver]   mede a taxa de acerto no portal real (o portal e o oraculo)
//   ocr <pasta> [solver]                le PNGs salvos, offline
//   avaliar <pasta> <rotulos.csv> [solver]   acerto offline contra rotulos
//   tensor <entrada> <saida>            grava o tensor 128x32 que a rede neural recebe
//   dump <entrada> <saida>              grava as variantes do pre-processamento (Tesseract)
// "solver" = neural (padrao, se o modelo existir) | tesseract
// O modo live exige o argumento de proposito: rodar sem argumentos nunca deve bater no portal
// (ele limita requisicoes por IP e bloqueia com HTTP 429).

const string baseUrl = "https://rastreamento.correios.com.br";
const string codigoTeste = "NN437873753BR";

if (args.Length == 0)
{
    Console.WriteLine("""
        Uso:
          live <tentativas> [pastaAmostras] [neural|tesseract]
          ocr <pasta> [neural|tesseract]
          avaliar <pasta> <rotulos.csv> [neural|tesseract]
          tensor <entrada> <saida>
          dump <entrada> <saida>
        """);
    return;
}

switch (args[0])
{
    case "tensor" when args.Length == 3:
        Tensores(args[1], args[2]);
        return;
    case "dump" when args.Length == 3:
        Variantes(args[1], args[2]);
        return;
    case "avaliar" when args.Length >= 3:
        Avaliar(args[1], args[2], args.Length > 3 ? args[3] : "neural");
        return;
    case "ocr" when args.Length >= 2:
        Ocr(args[1], args.Length > 2 ? args[2] : "neural");
        return;
    case "live" when args.Length >= 2 && int.TryParse(args[1], out var total):
        await AoVivo(total, args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : "neural");
        return;
    default:
        Console.WriteLine("Argumentos invalidos.");
        return;
}

static ICaptchaSolver CriarSolver(string nome)
{
    var tessdata = Path.Combine(AppContext.BaseDirectory, "tessdata");
    var modelo = Path.Combine(AppContext.BaseDirectory, "modelos", "captcha-crnn.onnx");

    if (nome == "tesseract" || !File.Exists(modelo))
    {
        Console.WriteLine("[solver: tesseract]");
        return new TesseractCaptchaSolver(tessdata);
    }

    Console.WriteLine("[solver: rede neural + fallback tesseract]");
    return new SolverComFallback(new RedeNeuralCaptchaSolver(modelo), new TesseractCaptchaSolver(tessdata));
}

// Acerto offline contra rotulos (arquivo,texto): texto inteiro e por caractere.
static void Avaliar(string pasta, string csv, string nome)
{
    using var solver = (IDisposable)CriarSolver(nome);
    int ok = 0, total = 0, okCar = 0, totalCar = 0;

    foreach (var linha in File.ReadLines(csv))
    {
        var partes = linha.Split(',');
        var arquivo = Path.Combine(pasta, partes[0]);
        if (partes.Length < 2 || !File.Exists(arquivo))
        {
            continue;
        }

        var esperado = partes[1].Trim();
        var lido = ((ICaptchaSolver)solver).Resolver(File.ReadAllBytes(arquivo)).Texto;
        total++;
        ok += lido == esperado ? 1 : 0;
        totalCar += esperado.Length;
        okCar += esperado.Where((c, i) => i < lido.Length && lido[i] == c).Count();
    }

    Console.WriteLine($"[{nome}] texto inteiro: {ok}/{total} = {100.0 * ok / total:F1}% | caracteres (posicao a posicao): {100.0 * okCar / totalCar:F1}%");
}

static void Ocr(string pasta, string nome)
{
    using var solver = (IDisposable)CriarSolver(nome);
    foreach (var arquivo in Directory.GetFiles(pasta, "*.png").Order())
    {
        var leitura = ((ICaptchaSolver)solver).Resolver(File.ReadAllBytes(arquivo));
        Console.WriteLine($"{Path.GetFileNameWithoutExtension(arquivo),-22} => {leitura.Texto,-8} ({leitura.Confianca:P0})");
    }
}

static void Variantes(string entrada, string saida)
{
    Directory.CreateDirectory(saida);
    foreach (var arquivo in Directory.GetFiles(entrada, "*.png"))
    {
        var variantes = CaptchaImagePreprocessor.GerarVariantes(File.ReadAllBytes(arquivo));
        for (var v = 0; v < variantes.Count; v++)
        {
            File.WriteAllBytes(Path.Combine(saida, $"{Path.GetFileNameWithoutExtension(arquivo)}_v{v}.png"), variantes[v]);
        }
    }
}

static void Tensores(string entrada, string saida)
{
    Directory.CreateDirectory(saida);
    foreach (var arquivo in Directory.GetFiles(entrada, "*.png"))
    {
        var mascara = CaptchaImagePreprocessor.GerarMascara(File.ReadAllBytes(arquivo));
        if (mascara is null)
        {
            Console.WriteLine($"sem letras: {Path.GetFileName(arquivo)}");
            continue;
        }

        var tensor = CaptchaImagePreprocessor.ParaTensor(mascara);
        using var img = new Image<L8>(CaptchaImagePreprocessor.TensorLargura, CaptchaImagePreprocessor.TensorAltura);
        for (var i = 0; i < tensor.Length; i++)
        {
            img[i % CaptchaImagePreprocessor.TensorLargura, i / CaptchaImagePreprocessor.TensorLargura] =
                new L8((byte)Math.Round(tensor[i] * 255));
        }

        img.SaveAsPng(Path.Combine(saida, Path.GetFileName(arquivo)));
    }
}

async Task AoVivo(int total, string? pastaAmostras, string nomeSolver)
{
    // 12 s entre tentativas: o portal bloqueia o IP (429) com rajadas de requisições.
    const int intervaloMs = 12000;
    await AoVivoInterno(total, pastaAmostras, nomeSolver, intervaloMs);
}

async Task AoVivoInterno(int total, string? pastaAmostras, string nomeSolver, int intervaloMs)
{
    if (pastaAmostras is not null)
    {
        Directory.CreateDirectory(pastaAmostras);
    }

    using var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true };
    using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");

    await http.GetAsync($"{baseUrl}/app/index.php");

    using var solverDescartavel = (IDisposable)CriarSolver(nomeSolver);
    var solver = (ICaptchaSolver)solverDescartavel;
    int acertos = 0, semLeitura = 0, feitas = 0;
    var resultados = new List<(double Confianca, bool Aceito)>();
    var relogio = Stopwatch.StartNew();

    for (var i = 1; i <= total; i++)
    {
        var imagem = await BaixarAsync(http, $"{baseUrl}/core/securimage/securimage_show.php?{Random.Shared.Next()}");
        var leitura = solver.Resolver(imagem);
        feitas++;

        if (!leitura.TemTexto)
        {
            semLeitura++;
            Console.WriteLine($"{i,3}: (sem leitura)");
            continue;
        }

        var resposta = Encoding.UTF8.GetString(await BaixarAsync(
            http, $"{baseUrl}/app/resultado.php?objeto={codigoTeste}&captcha={leitura.Texto}&mqs=S"));
        var aceito = !resposta.Contains("Captcha inv", StringComparison.OrdinalIgnoreCase);

        if (aceito)
        {
            acertos++;
        }

        resultados.Add((leitura.Confianca, aceito));

        if (pastaAmostras is not null)
        {
            // Imagens aceitas têm rótulo certo (o portal confirmou); as recusadas ficam sem rótulo confiável.
            var sufixo = aceito ? "ok" : "x";
            await File.WriteAllBytesAsync(
                Path.Combine(pastaAmostras, $"{i:D3}_{leitura.Texto}_{sufixo}_{leitura.Confianca * 100:F0}.png"), imagem);
        }

        Console.WriteLine($"{i,3}: {leitura.Texto,-8} conf {leitura.Confianca * 100,3:F0}% {(aceito ? "OK" : "x")}");
        await Task.Delay(intervaloMs);
    }

    Console.WriteLine();
    Console.WriteLine("Acerto por faixa de confianca (so leituras submetidas):");
    foreach (var (minimo, maximo) in new[] { (0.0, 0.6), (0.6, 0.8), (0.8, 0.9), (0.9, 0.95), (0.95, 1.01) })
    {
        var faixa = resultados.Where(r => r.Confianca >= minimo && r.Confianca < maximo).ToList();
        if (faixa.Count > 0)
        {
            Console.WriteLine($"  {minimo:P0}-{Math.Min(maximo, 1):P0}: {faixa.Count(r => r.Aceito)}/{faixa.Count} = {100.0 * faixa.Count(r => r.Aceito) / faixa.Count:F0}%");
        }
    }

    Console.WriteLine(
        $"Tentativas: {feitas} | aceitas: {acertos} ({100.0 * acertos / Math.Max(1, feitas):F1}%) | sem leitura: {semLeitura} | {relogio.Elapsed.TotalSeconds:F0}s");
}

// O portal responde 429 quando há muitas requisições seguidas: espera e tenta de novo.
static async Task<byte[]> BaixarAsync(HttpClient http, string url)
{
    for (var espera = 30; ; espera = Math.Min(espera * 2, 600))
    {
        var r = await http.GetAsync(url);
        if (r.StatusCode != HttpStatusCode.TooManyRequests)
        {
            r.EnsureSuccessStatusCode();
            return await r.Content.ReadAsByteArrayAsync();
        }

        Console.WriteLine($"    429 do portal; aguardando {espera}s...");
        await Task.Delay(TimeSpan.FromSeconds(espera));
    }
}
