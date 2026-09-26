using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace NetSpeed.Captcha;

/// <summary>
/// Limpa a imagem do CAPTCHA do portal (Securimage) e a prepara para o OCR.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que dá para limpar por cor.</b> O Securimage desenha cada camada com um tom de cinza
/// próprio e <em>sem antialiasing</em>: letras e pontinhos de ruído em ~140, as linhas grossas
/// em ~112 e as finas em ~117 sobre fundo branco. Isolar o tom das letras remove as linhas de
/// uma vez — o que uma binarização por limiar (Otsu) não consegue, porque as linhas são mais
/// escuras que as letras e sobreviveriam ao corte.
/// </para>
/// <para>
/// Etapas: (1) máscara do tom das letras; (2) descarta componentes conexos pequenos (os pontos
/// de ruído); (3) preenche as "falhas" que uma linha cruzando a letra deixou (só sobre pixels
/// de linha, para não colar letras vizinhas); (4) recorta, amplia e devolve preto-sobre-branco.
/// </para>
/// </remarks>
public static class CaptchaImagePreprocessor
{
    /// <summary>Tom de cinza das letras no Securimage do portal.</summary>
    public const byte TomDasLetras = 140;

    private const int Tolerancia = 8;

    /// <summary>Componentes menores que isso (em pixels) são ruído, não letra.</summary>
    private const int AreaMinimaComponente = 14;

    /// <summary>Distância máxima (px) entre letra e letra para considerar que uma linha atravessou uma letra só.</summary>
    private const int AlcanceDoPreenchimento = 2;

    /// <summary>
    /// Gera as variantes de imagem (escala/estratégia) que o solver vai submeter ao OCR.
    /// Mais de uma variante permite votação: um erro isolado de uma leitura é vencido pelas demais.
    /// </summary>
    public static IReadOnlyList<byte[]> GerarVariantes(byte[] imagem)
    {
        ArgumentNullException.ThrowIfNull(imagem);

        using var origem = Image.Load<Rgb24>(imagem);
        var (letras, linhas) = SepararCamadas(origem);
        var limpa = RemoverRuido(letras);

        var comFalhasPreenchidas = PreencherFalhas(limpa, linhas);
        comFalhasPreenchidas = RemoverRuido(comFalhasPreenchidas);

        var variantes = new List<byte[]>();
        foreach (var (mascara, escala) in new[]
                 {
                     (comFalhasPreenchidas, 3),
                     (comFalhasPreenchidas, 4),
                     (limpa, 3),
                     (Engrossar(comFalhasPreenchidas), 3)
                 })
        {
            var png = Renderizar(mascara, escala);
            if (png is not null)
            {
                variantes.Add(png);
            }
        }

        return variantes;
    }

    /// <summary>Largura do tensor de entrada do reconhecedor neural.</summary>
    public const int TensorLargura = 128;

    /// <summary>Altura do tensor de entrada do reconhecedor neural.</summary>
    public const int TensorAltura = 32;

    /// <summary>
    /// Máscara limpa das letras (true = tinta), no tamanho original da imagem. É a entrada comum
    /// do reconhecedor neural e a base das variantes do Tesseract. Devolve <c>null</c> se não
    /// sobrou nenhuma letra depois da limpeza.
    /// </summary>
    public static bool[,]? GerarMascara(byte[] imagem)
    {
        ArgumentNullException.ThrowIfNull(imagem);

        using var origem = Image.Load<Rgb24>(imagem);
        var (letras, linhas) = SepararCamadas(origem);
        var mascara = RemoverRuido(PreencherFalhas(RemoverRuido(letras), linhas));

        return TemTinta(mascara) ? mascara : null;
    }

    /// <summary>
    /// Converte a máscara no tensor do reconhecedor: recorta no retângulo das letras, redimensiona
    /// para <see cref="TensorLargura"/> x <see cref="TensorAltura"/> (filtro triangular = média de
    /// área) e devolve a cobertura de tinta em 0..1, linha a linha.
    /// </summary>
    public static float[] ParaTensor(bool[,] mascara)
    {
        ArgumentNullException.ThrowIfNull(mascara);

        var h = mascara.GetLength(0);
        var w = mascara.GetLength(1);
        int minX = w, minY = h, maxX = -1, maxY = -1;

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (mascara[y, x])
                {
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }
        }

        var cw = maxX - minX + 1;
        var ch = maxY - minY + 1;
        using var recorte = new Image<L8>(cw, ch, new L8(0));
        for (var y = 0; y < ch; y++)
        {
            for (var x = 0; x < cw; x++)
            {
                if (mascara[minY + y, minX + x])
                {
                    recorte[x, y] = new L8(255);
                }
            }
        }

        recorte.Mutate(c => c.Resize(TensorLargura, TensorAltura, KnownResamplers.Triangle));

        var tensor = new float[TensorLargura * TensorAltura];
        for (var y = 0; y < TensorAltura; y++)
        {
            for (var x = 0; x < TensorLargura; x++)
            {
                tensor[(y * TensorLargura) + x] = recorte[x, y].PackedValue / 255f;
            }
        }

        return tensor;
    }

    private static bool TemTinta(bool[,] mascara)
    {
        foreach (var pixel in mascara)
        {
            if (pixel)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Uma variante única (a principal), útil para depurar e nos testes.</summary>
    public static byte[]? GerarPrincipal(byte[] imagem) =>
        GerarVariantes(imagem).FirstOrDefault();

    private static (bool[,] Letras, bool[,] Linhas) SepararCamadas(Image<Rgb24> imagem)
    {
        var w = imagem.Width;
        var h = imagem.Height;
        var letras = new bool[h, w];
        var linhas = new bool[h, w];

        imagem.ProcessPixelRows(acessor =>
        {
            for (var y = 0; y < h; y++)
            {
                var linha = acessor.GetRowSpan(y);
                for (var x = 0; x < w; x++)
                {
                    var p = linha[x];
                    var cinza = (p.R + p.G + p.B) / 3;

                    if (Math.Abs(cinza - TomDasLetras) <= Tolerancia)
                    {
                        letras[y, x] = true;
                    }
                    else if (cinza < 200)
                    {
                        // Qualquer outro tom escuro é linha (grossa ou fina).
                        linhas[y, x] = true;
                    }
                }
            }
        });

        return (letras, linhas);
    }

    /// <summary>Remove componentes conexos (8-vizinhança) menores que o mínimo.</summary>
    internal static bool[,] RemoverRuido(bool[,] mascara)
    {
        var h = mascara.GetLength(0);
        var w = mascara.GetLength(1);
        var saida = new bool[h, w];
        var visitado = new bool[h, w];
        var pilha = new Stack<(int Y, int X)>();
        var componente = new List<(int Y, int X)>();

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (!mascara[y, x] || visitado[y, x])
                {
                    continue;
                }

                componente.Clear();
                pilha.Push((y, x));
                visitado[y, x] = true;

                while (pilha.Count > 0)
                {
                    var (cy, cx) = pilha.Pop();
                    componente.Add((cy, cx));

                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            var ny = cy + dy;
                            var nx = cx + dx;
                            if (ny < 0 || nx < 0 || ny >= h || nx >= w ||
                                visitado[ny, nx] || !mascara[ny, nx])
                            {
                                continue;
                            }

                            visitado[ny, nx] = true;
                            pilha.Push((ny, nx));
                        }
                    }
                }

                if (componente.Count >= AreaMinimaComponente)
                {
                    foreach (var (cy, cx) in componente)
                    {
                        saida[cy, cx] = true;
                    }
                }
            }
        }

        return saida;
    }

    /// <summary>
    /// Onde uma linha atravessou a letra, o pixel virou "linha". Se ele tem letra dos dois lados
    /// (horizontal ou vertical), era letra: é devolvido à máscara.
    /// </summary>
    private static bool[,] PreencherFalhas(bool[,] letras, bool[,] linhas)
    {
        var h = letras.GetLength(0);
        var w = letras.GetLength(1);
        var saida = (bool[,])letras.Clone();

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (letras[y, x] || !linhas[y, x])
                {
                    continue;
                }

                if (TemLetraNosDoisLados(letras, y, x, 1, 0, AlcanceDoPreenchimento) ||
                    TemLetraNosDoisLados(letras, y, x, 0, 1, AlcanceDoPreenchimento) ||
                    TemLetraNosDoisLados(letras, y, x, 1, 1, AlcanceDoPreenchimento) ||
                    TemLetraNosDoisLados(letras, y, x, 1, -1, AlcanceDoPreenchimento))
                {
                    saida[y, x] = true;
                }
            }
        }

        return saida;
    }

    private static bool TemLetraNosDoisLados(bool[,] m, int y, int x, int dy, int dx, int alcance)
    {
        var h = m.GetLength(0);
        var w = m.GetLength(1);
        var antes = false;
        var depois = false;

        for (var k = 1; k <= alcance; k++)
        {
            var ay = y - (dy * k);
            var ax = x - (dx * k);
            var by = y + (dy * k);
            var bx = x + (dx * k);

            antes |= ay >= 0 && ax >= 0 && ay < h && ax < w && m[ay, ax];
            depois |= by >= 0 && bx >= 0 && by < h && bx < w && m[by, bx];
        }

        return antes && depois;
    }

    /// <summary>Dilatação 3x3: engrossa o traço, ajudando o OCR em letras muito finas.</summary>
    private static bool[,] Engrossar(bool[,] mascara)
    {
        var h = mascara.GetLength(0);
        var w = mascara.GetLength(1);
        var saida = new bool[h, w];

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (!mascara[y, x])
                {
                    continue;
                }

                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var ny = y + dy;
                        var nx = x + dx;
                        if (ny >= 0 && nx >= 0 && ny < h && nx < w)
                        {
                            saida[ny, nx] = true;
                        }
                    }
                }
            }
        }

        return saida;
    }

    /// <summary>Recorta no retângulo das letras, amplia e produz PNG preto-sobre-branco com margem.</summary>
    private static byte[]? Renderizar(bool[,] mascara, int escala)
    {
        var h = mascara.GetLength(0);
        var w = mascara.GetLength(1);
        int minX = w, minY = h, maxX = -1, maxY = -1;

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (!mascara[y, x])
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }

        if (maxX < 0)
        {
            return null; // Nada além de ruído: imagem inutilizável.
        }

        const int margem = 4;
        var cw = maxX - minX + 1;
        var ch = maxY - minY + 1;
        using var saida = new Image<L8>(cw + (2 * margem), ch + (2 * margem), new L8(255));

        for (var y = 0; y < ch; y++)
        {
            for (var x = 0; x < cw; x++)
            {
                if (mascara[minY + y, minX + x])
                {
                    saida[margem + x, margem + y] = new L8(0);
                }
            }
        }

        // Ampliação bicúbica (não por replicação de pixel): as bordas ficam suaves, que é o que
        // o LSTM do Tesseract viu no treino. Replicar pixels deixaria degraus que ele lê como traço.
        saida.Mutate(c => c.Resize(saida.Width * escala, saida.Height * escala, KnownResamplers.Bicubic));

        using var ms = new MemoryStream();
        saida.SaveAsPng(ms);
        return ms.ToArray();
    }
}
