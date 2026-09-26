namespace NetSpeed.Executor;

/// <summary>Argumentos da linha de comando já interpretados.</summary>
internal sealed class Argumentos
{
    public List<string> Codigos { get; } = [];

    public string? Saida { get; private set; }

    /// <summary>Valores que sobrescrevem o appsettings.json (chave hierárquica => valor).</summary>
    public Dictionary<string, string?> Sobrescritas { get; } = [];

    public static Argumentos Ler(string[] args)
    {
        var resultado = new Argumentos();

        for (var i = 0; i < args.Length; i++)
        {
            var atual = args[i];

            switch (atual)
            {
                case "--codigo":
                    resultado.Codigos.Add(Valor(args, ref i, atual));
                    break;
                case "--arquivo":
                    resultado.Codigos.AddRange(LerArquivo(Valor(args, ref i, atual)));
                    break;
                case "--saida":
                    resultado.Saida = Valor(args, ref i, atual);
                    break;
                case "--visivel":
                    resultado.Sobrescritas["AppSettings:Navegador:Headless"] = "false";
                    break;
                case "--max-tentativas":
                    resultado.Sobrescritas["AppSettings:Captcha:MaxTentativas"] = Inteiro(Valor(args, ref i, atual), atual);
                    break;
                case "--paralelismo":
                    resultado.Sobrescritas["AppSettings:Lote:MaxParalelismo"] = Inteiro(Valor(args, ref i, atual), atual);
                    break;
                case "--debug-captcha":
                    resultado.Sobrescritas["AppSettings:Captcha:DiretorioDebug"] = Valor(args, ref i, atual);
                    break;
                default:
                    if (atual.StartsWith('-'))
                    {
                        throw new ArgumentException($"Opção desconhecida: {atual}");
                    }

                    resultado.Codigos.Add(atual);
                    break;
            }
        }

        return resultado;
    }

    private static string Valor(string[] args, ref int i, string opcao)
    {
        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"A opção {opcao} exige um valor.");
        }

        return args[++i];
    }

    private static string Inteiro(string valor, string opcao) =>
        int.TryParse(valor, out var n) && n > 0
            ? n.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : throw new ArgumentException($"A opção {opcao} exige um inteiro positivo (recebido: '{valor}').");

    private static IEnumerable<string> LerArquivo(string caminho)
    {
        if (!File.Exists(caminho))
        {
            throw new ArgumentException($"Arquivo não encontrado: {caminho}");
        }

        return File.ReadAllLines(caminho)
            .Select(l => l.Split('#')[0].Trim())
            .Where(l => l.Length > 0);
    }
}
