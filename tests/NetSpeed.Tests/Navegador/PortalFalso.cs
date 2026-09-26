using System.Net;
using System.Text;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace NetSpeed.Tests.Navegador;

/// <summary>
/// Portal dos Correios "de mentira", servido em localhost, com os mesmos ids e o mesmo
/// comportamento de DOM do portal real (campo <c>#captcha</c> ganha a classe <c>invalid</c>;
/// erros aparecem em <c>#alerta.aberto .msg</c>; o resultado vai para <c>#tabs-rastreamento</c>).
/// </summary>
/// <remarks>
/// Permite testar a <c>PaginaCorreios</c> (Selenium de verdade, Chrome de verdade) sem depender
/// da internet nem do rate limit do portal — inclusive os caminhos que só acontecem com dados
/// específicos, como "objeto não encontrado".
/// </remarks>
internal sealed class PortalFalso : IDisposable
{
    public const string CaptchaCorreto = "ok123";
    public const string CodigoInexistente = "NN000000000BR";

    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly string _htmlResultado;
    private int _imagens;

    public PortalFalso()
    {
        _htmlResultado = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rastro-4-eventos.html"));

        // Porta livre sorteada pelo sistema.
        var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var porta = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();

        UrlBase = $"http://localhost:{porta}";
        _listener.Prefixes.Add(UrlBase + "/");
        _listener.Start();
        _ = Task.Run(AtenderAsync);
    }

    public string UrlBase { get; }

    public string UrlPortal => $"{UrlBase}/app/index.php";

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Close();
    }

    private async Task AtenderAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext contexto;
            try
            {
                contexto = await _listener.GetContextAsync();
            }
            catch (Exception) when (_cts.IsCancellationRequested || !_listener.IsListening)
            {
                return;
            }

            try
            {
                Responder(contexto);
            }
#pragma warning disable CA1031 // Servidor de teste: uma requisição ruim não pode derrubar o laço.
            catch (Exception)
            {
                contexto.Response.StatusCode = 500;
                contexto.Response.Close();
            }
#pragma warning restore CA1031
        }
    }

    private void Responder(HttpListenerContext contexto)
    {
        var caminho = contexto.Request.Url!.AbsolutePath;

        if (caminho == "/app/index.php")
        {
            Escrever(contexto, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Pagina));
        }
        else if (caminho == "/captcha.png")
        {
            Escrever(contexto, "image/png", NovaImagem());
        }
        else if (caminho == "/app/resultado.php")
        {
            var consulta = System.Web.HttpUtility.ParseQueryString(contexto.Request.Url.Query);
            object corpo;

            if (consulta["captcha"] != CaptchaCorreto)
            {
                corpo = new { erro = "true", mensagem = "Captcha inválido" };
            }
            else if (consulta["objeto"] == CodigoInexistente)
            {
                corpo = new { erro = "true", mensagem = "Objeto não encontrado" };
            }
            else
            {
                corpo = new { html = _htmlResultado };
            }

            Escrever(contexto, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(corpo));
        }
        else
        {
            contexto.Response.StatusCode = 404;
            contexto.Response.Close();
        }
    }

    private static void Escrever(HttpListenerContext contexto, string tipo, byte[] dados)
    {
        contexto.Response.ContentType = tipo;
        contexto.Response.ContentLength64 = dados.Length;
        contexto.Response.OutputStream.Write(dados);
        contexto.Response.Close();
    }

    /// <summary>PNG 60x30 diferente a cada chamada (cor derivada do contador).</summary>
    private byte[] NovaImagem()
    {
        var n = Interlocked.Increment(ref _imagens);
        using var img = new Image<Rgb24>(60, 30, new Rgb24((byte)(n * 37), (byte)(n * 91), (byte)(n * 53)));
        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        return ms.ToArray();
    }

    private const string Pagina = """
        <!DOCTYPE html><html lang="pt-BR"><head><meta charset="utf-8"><title>Rastreamento (falso)</title></head>
        <body>
          <input type="text" id="objeto" name="objeto">
          <div class="campos captcha">
            <div class="campo"><div class="controle">
              <img id="captcha_image" src="/captcha.png?0" alt="CAPTCHA">
              <a href="#" id="captcha_refresh_btn" title="Refresh Image"><i>r</i></a>
            </div></div>
            <div class="campo"><div class="controle">
              <input type="text" name="captcha" id="captcha" class="form-control">
              <button type="button" id="b-pesquisar">Consultar</button>
            </div><div class="mensagem"></div></div>
          </div>
          <div id="tabs-rastreamento"></div>
          <div id="alerta"><div class="msg"></div></div>
          <script>
            let n = 0;
            const refresh = () => { document.getElementById('captcha_image').src = '/captcha.png?' + (++n); };
            document.getElementById('captcha_refresh_btn').addEventListener('click', e => { e.preventDefault(); refresh(); });
            document.getElementById('b-pesquisar').addEventListener('click', async () => {
              const cap = document.getElementById('captcha');
              const msg = document.querySelector('.campos.captcha .mensagem');
              const alerta = document.getElementById('alerta');
              cap.classList.remove('invalid'); msg.textContent = '';
              alerta.classList.add('aberto'); alerta.querySelector('.msg').textContent = 'Buscando...';
              document.getElementById('tabs-rastreamento').innerHTML = '';
              await new Promise(r => setTimeout(r, 200));
              const r = await (await fetch('/app/resultado.php?objeto=' + document.getElementById('objeto').value + '&captcha=' + cap.value)).json();
              if (r.erro) {
                if (r.mensagem === 'Captcha inválido') { alerta.classList.remove('aberto'); cap.classList.add('invalid'); msg.textContent = r.mensagem; }
                else { alerta.querySelector('.msg').textContent = r.mensagem; }
              } else {
                alerta.classList.remove('aberto');
                document.getElementById('tabs-rastreamento').innerHTML = r.html;
                const ver = document.getElementById('a-ver-mais');
                if (ver) ver.addEventListener('click', () => {
                  document.querySelector('#ver-mais').style.display = 'none';
                  document.querySelector('#ver-rastro-unico').style.display = 'block';
                });
              }
              refresh();
            });
          </script>
        </body></html>
        """;
}
