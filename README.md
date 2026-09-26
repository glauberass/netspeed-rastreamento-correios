# Rastreamento Correios — Desafio NetSpeed (Desenvolvedor de Automações Web/RPA)

Automação em **C# / .NET 8** que recebe um código de rastreamento, acessa o portal dos Correios,
**resolve sozinha o CAPTCHA de imagem**, lê **todos os eventos** do histórico e entrega o resultado
em **JSON estruturado**. Vem com linha de comando, API REST + interface web, logs, retries,
testes e o "Desafio Extra" completo.

```bash
dotnet run --project src/NetSpeed.Executor -- NN437873753BR
```

> **Antes de tudo — duas coisas que o avaliador precisa saber**
> 1. O portal aplica **rate limit por IP** (HTTP 429). A solução foi desenhada em torno disso
>    (ver [Limitações](#limitações-conhecidas)). Rode poucas consultas seguidas.
> 2. A taxa de acerto do CAPTCHA por imagem **não é 100%** e está medida e explicada em
>    [CAPTCHA](#captcha-estratégia-e-taxa-de-acerto).

---

## Sumário

1. [Como executar](#como-executar)
2. [Exemplo de uso e de JSON](#exemplo-de-uso-e-de-json)
3. [Tecnologia e bibliotecas](#tecnologia-e-bibliotecas)
4. [Estratégia da automação](#estratégia-da-automação)
5. [Como os elementos da página são localizados e interpretados](#como-os-elementos-da-página-são-localizados-e-interpretados)
6. [Do HTML ao JSON](#do-html-ao-json)
7. [CAPTCHA: estratégia e taxa de acerto](#captcha-estratégia-e-taxa-de-acerto)
8. [Tratamento de erros](#tratamento-de-erros)
9. [Desafio Extra — o que foi feito](#desafio-extra--o-que-foi-feito)
10. [Estrutura do repositório](#estrutura-do-repositório)
11. [Decisões técnicas](#decisões-técnicas)
12. [Limitações conhecidas](#limitações-conhecidas)
13. [Melhorias futuras](#melhorias-futuras)

---

## Como executar

### Requisitos

| Item | Detalhe |
|---|---|
| Sistema | Windows x64 (o Tesseract usado como *fallback* traz binários nativos de Windows; a rede neural principal é multiplataforma) |
| .NET SDK | 8.0 ou superior (`dotnet --version`) |
| Navegador | **Google Chrome** instalado (o Selenium Manager baixa o *chromedriver* compatível sozinho na primeira execução — precisa de internet) |
| Internet | Acesso a `rastreamento.correios.com.br` |

Não há mais nada a instalar: o modelo do CAPTCHA (`modelos/captcha-crnn.onnx`) e os dados do
Tesseract (`tessdata/`) já estão no repositório e são copiados para a pasta de saída no build.

### Build e testes

```bash
dotnet build          # 0 avisos (TreatWarningsAsErrors)
dotnet test           # testes unitários e de fluxo (sem navegador e sem rede)
# + testes com Chrome real contra um portal falso local (sem tocar nos Correios):
#   NETSPEED_TESTE_NAVEGADOR=1 dotnet test
```

### Linha de comando

```bash
# um código (o valor NÃO está fixo no código-fonte: vem por parâmetro)
dotnet run --project src/NetSpeed.Executor -- NN437873753BR

# vários códigos, gravando o JSON em arquivo
dotnet run --project src/NetSpeed.Executor -- NN437873753BR AA123456789BR --saida saida/lote.json

# a partir de um arquivo (um código por linha; # comenta)
dotnet run --project src/NetSpeed.Executor -- --arquivo codigos.txt

# ver o navegador abrindo / mais tentativas de CAPTCHA / guardar as imagens tentadas
dotnet run --project src/NetSpeed.Executor -- NN437873753BR --visivel --max-tentativas 25 --debug-captcha debug
```

O JSON sai no **stdout** e os logs no **stderr** (dá para fazer `... > resultado.json`). Código de
saída: `0` todas com sucesso · `1` ao menos uma falhou · `2` uso incorreto.

### API REST + interface web

```bash
dotnet run --project src/NetSpeed.Web        # http://localhost:5180
```

| Método | Rota | Descrição |
|---|---|---|
| `POST` | `/api/rastreamentos` | Corpo `{"codigos":["NN437873753BR"]}`. Enfileira e responde **202** com o `id` do job (execução assíncrona). |
| `GET` | `/api/rastreamentos/{id}` | Situação (`pendente`/`executando`/`concluido`/`falhou`) e os resultados quando prontos. |
| `GET` | `/api/rastreamentos` | Jobs recentes. |
| `GET` | `/api/saude` | Verificação de saúde. |

A página inicial tem um formulário simples: informe os códigos e acompanhe o resultado.

### Configuração

`src/NetSpeed.Executor/appsettings.json` (e o do Web): timeouts, número de tentativas do CAPTCHA,
retries e *backoff*, paralelismo do lote, modo headless, pasta de saída e de logs.

---

## Exemplo de uso e de JSON

Exemplos gerados pela própria ferramenta ficam em [`exemplos/`](exemplos/).

```json
{
  "codigoObjeto": "NN437873753BR",
  "consultadoEm": "2026-09-26T08:47:07.6892597-03:00",
  "sucesso": true,
  "erro": null,
  "tipoPostal": "PACKET STANDARD IMPORTAÇÃO",
  "previsaoEntrega": null,
  "eventos": [
    {
      "status": "Objeto entregue ao destinatário",
      "descricao": "Queremos te ouvir! Responda a uma pesquisa rápida e nos ajude a melhorar a sua experiência: https://survey3.medallia.com/?correios-nps-sms-sro&obj=NN437873753BR",
      "dataHora": "2026-08-31T16:04:00",
      "dataHoraOriginal": "31/08/2026 16:04",
      "local": "Unidade de Distribuição, Sao Jose do Rio Preto - SP",
      "origem": null,
      "destino": null,
      "informacoesAdicionais": []
    },
    {
      "status": "Objeto saiu para entrega ao destinatário",
      "descricao": "É preciso ter alguém no endereço para receber o carteiro",
      "dataHora": "2026-08-31T09:22:00",
      "dataHoraOriginal": "31/08/2026 09:22",
      "local": "Sao Jose do Rio Preto - SP",
      "origem": null,
      "destino": null,
      "informacoesAdicionais": []
    },
    {
      "status": "Objeto em transferência - por favor aguarde",
      "descricao": null,
      "dataHora": "2026-08-28T08:52:00",
      "dataHoraOriginal": "28/08/2026 08:52",
      "local": null,
      "origem": "Unidade de Tratamento, Sao Jose do Rio Preto - SP",
      "destino": "Unidade de Distribuição, Sao Jose do Rio Preto - SP",
      "informacoesAdicionais": []
    },
    {
      "...": "mais 9 eventos (veja exemplos/NN437873753BR.json)"
    }
  ],
  "tentativasCaptcha": [
    {
      "numero": 1,
      "texto": "tl9le",
      "aceito": true,
      "duracaoMs": 3984,
      "observacao": null
    }
  ],
  "duracaoMs": 9904
}
```

---

## Tecnologia e bibliotecas

| Peça | Escolha | Papel |
|---|---|---|
| Linguagem/plataforma | **C# 12 / .NET 8** | Preferência do enunciado; toda a solução |
| Automação do navegador | **Selenium WebDriver 4 + Chrome** | Navegar, preencher, clicar, executar JS no DOM |
| JavaScript | `IJavaScriptExecutor` | Extrair os pixels exatos da imagem do CAPTCHA (`canvas.toDataURL`) e checar o estado da página |
| Interpretação do HTML | **HtmlAgilityPack** | Parser do resultado, testável sem navegador |
| CAPTCHA — solver principal | **CRNN + CTC treinada** (PyTorch) exportada para **ONNX**, executada com **Microsoft.ML.OnnxRuntime** | Ler as letras |
| CAPTCHA — pré-processamento | **SixLabors.ImageSharp** | Separar letras de ruído por cor |
| CAPTCHA — fallback | **Tesseract 5** (pacote `Tesseract`) | Segunda opinião se a rede não ler nada |
| Padrão de fluxo | **Pipelining** (`IPipe`/`PipelineGroup`) | Mesma estrutura do RPA de produção |
| Logs | **Serilog** (console + arquivo + JSON estruturado) | Diagnóstico |
| Injeção de dependência | `Microsoft.Extensions.DependencyInjection` | Composição |
| API | ASP.NET Core Minimal API | Execução assíncrona por HTTP |
| Testes | **xUnit** | 70 testes (6 com Chrome real contra um portal falso local) |

Por que Selenium e não CefSharp/Playwright/HTTP puro: veja
[`docs/decisoes-tecnicas.md`](docs/decisoes-tecnicas.md#1-selenium--chrome-em-vez-de-cefsharp-playwright-ou-http-puro).

---

## Estratégia da automação

```
Executor / Web ──► RastreamentoService ──► PoliticaDeRetry ──► Pipeline "ConsultarObjeto"
                                                                  │
   1. AbrirPagina      abre o Chrome e carrega o portal          ◄─┤
   2. QuebrarCaptcha   lê imagem → OCR → submete → valida → repete
   3. ExtrairEventos   HTML do resultado → RastroHtmlParser → eventos
   4. (finally) FinalizarConsulta   fecha o Chrome, sempre
```

1. Valida e normaliza o código (`^[A-Z]{2}[0-9]{9}[A-Z]{2}$`) **antes** de abrir o navegador.
2. Abre o portal e espera a imagem do CAPTCHA carregar de verdade (`naturalWidth > 0`).
3. Laço do CAPTCHA (`ResolvedorDeCaptchaNoPortal`): obtém a imagem → solver → preenche `#objeto`
   e `#captcha` → clica em *Consultar* → interpreta a resposta (sucesso / "Captcha inválido" /
   mensagem de erro) → se recusado, a imagem já foi renovada pelo portal e o ciclo recomeça.
   **Todas as tentativas são registradas** (texto lido, aceito ou não, duração).
4. Com o CAPTCHA aceito, clica em *"Mais informações"* (o portal só mostra 3 eventos até então) e
   captura o HTML da área de resultado.
5. Parser transforma o HTML em eventos; o serviço monta o JSON.
6. O Chrome é sempre encerrado (bloco `finally` num pipeline separado).

Falhas transitórias (portal fora do ar, bloqueio 429, timeout) repetem **a consulta inteira** com
um navegador novo e *backoff* exponencial; respostas definitivas (objeto inexistente, CAPTCHA
esgotado, código inválido) não são repetidas.

---

## Como os elementos da página são localizados e interpretados

**Localização** — sempre por `id`, os mesmos que o JavaScript do próprio portal usa:

| Elemento | Seletor | Uso |
|---|---|---|
| Código do objeto | `#objeto` | preencher |
| Imagem do CAPTCHA | `#captcha_image` | ler pixels via `canvas` |
| Campo do CAPTCHA | `#captcha` | preencher; recebe a classe `invalid` quando recusado |
| Botão | `#b-pesquisar` | consultar |
| Renovar imagem | `#captcha_refresh_btn` | nova imagem |
| Área de resultado | `#tabs-rastreamento` | HTML dos eventos |
| Mais informações | `#a-ver-mais` | expandir a lista completa |
| Mensagens do portal | `#alerta.aberto .msg` | erros (ex.: objeto não encontrado); "Buscando…" é ignorado |

Nada de XPath posicional: se um `id` sumir, o erro é explícito (`layoutAlterado`) em vez de o robô
clicar no elemento errado. A resposta é detectada por **espera ativa de condição** (o `li.step`
apareceu? o campo ficou `invalid`? o alerta abriu?), nunca por `Thread.Sleep`.

**A imagem do CAPTCHA** é vinculada à sessão (cookie). Baixar a URL por fora entregaria *outra*
imagem, inútil para esta sessão. Por isso a imagem exibida é copiada para um `canvas` e exportada
em PNG — os mesmos pixels que o usuário vê, sem *screenshot* (que sofreria com escala/DPI).

**Interpretação dos eventos** — o portal monta cada evento como `li.step > .step-content` com
parágrafos `p.text-head` (status e mensagens) e `p.text-content` (local, data). O parser segue as
regras do próprio `rastroUnico.js` do portal:

| Situação | Como aparece | Como vira JSON |
|---|---|---|
| Evento comum | 1 linha de local | `local` |
| Transferência | linhas "de …" e "para …" | `origem` e `destino` (sem `local`) |
| Entrega | "Pela Unidade…, cidade" | `local` |
| Mensagem extra | `p.text-head` adicional | `descricao` |
| Prazo de retirada | dentro do status | `informacoesAdicionais` |
| Data | `dd/MM/yyyy HH:mm` no último parágrafo | `dataHora` ISO + `dataHoraOriginal` |

---

## Do HTML ao JSON

Modelagem (`EventoRastreamento`, `RastreamentoResultado`), principais decisões:

- **`eventos` na ordem do portal** (mais recente primeiro) e **todos** preservados: a lista
  completa vem de `#ver-rastro-unico`, não da visão resumida com "Mais informações".
- Campo que o portal não trouxe = **`null`** (nunca string vazia, nunca omitido): esquema estável.
- `dataHora` em ISO-8601 (horário de Brasília, sem fuso); `dataHoraOriginal` guarda o texto cru.
- `tentativasCaptcha` documenta o que aconteceu com o CAPTCHA em cada consulta.
- Erros têm `codigo` estável em texto (lista na próxima seção).
- Uma consulta → objeto; várias → array desses mesmos objetos.

---

## CAPTCHA: estratégia e taxa de acerto

O portal usa o **Securimage** (imagem 215×80: letras minúsculas/dígitos deformados + linhas + pontos).

### Estratégia em duas etapas

**1. Limpeza por cor (`CaptchaImagePreprocessor`).** O Securimage desenha cada camada num tom de
cinza próprio e sem antialiasing: **letras e pontos ≈ 140**, linhas grossas ≈ 112, finas ≈ 117.
Ficar só com o tom 140, descartar componentes minúsculos (pontos) e recompor as falhas onde uma
linha atravessou a letra deixa uma máscara preto-e-branco limpa das letras. (Binarização por
limiar não funcionaria: as linhas são *mais escuras* que as letras.)

**2. Reconhecimento por rede neural (`RedeNeuralCaptchaSolver`).** Uma CRNN pequena (~0,4 M de
parâmetros: convoluções → BiGRU → CTC) treinada para este CAPTCHA, executada em C# via ONNX
Runtime em milissegundos. Foi treinada com CAPTCHAs **sintéticos** (mesma família de fonte, mesmas
deformações e defeitos de limpeza) e ajustada com amostras **reais** rotuladas. Todo o
treinamento é reprodutível em [`tools/treinamento`](tools/treinamento/README.md).

O **Tesseract** ficou como *fallback*: com o mesmo pré-processamento ele acerta bem menos
(medido abaixo), porque seu modelo de linguagem genérico não conhece essas letras deformadas.

### O portal é o oráculo — e é assim que se mede

Só o portal sabe se o texto está certo. A taxa de acerto abaixo foi medida **contra o portal
real** (`tools/NetSpeed.CaptchaCalibrador live …`): busca uma imagem, lê, submete e vê se foi
aceito.

| Medida | Resultado |
|---|---|
| **Rede neural final, ao vivo, 40 tentativas** | **29 aceitas = 72,5%** por imagem (IC 95%: 57%–84%) |
| Rede treinada só com sintéticos (versão anterior), ao vivo, 40 tentativas | 25 aceitas = 62,5% (IC 95%: 47%–76%) |
| Tesseract (fallback), offline, 148 imagens rotuladas | 14,2% do texto inteiro |

Confiança da rede (modelo final, ao vivo): leituras com confiança ≥ 95% foram aceitas em 27/33 (82%);
abaixo disso, 2/7. Detalhes, método e limitações da medida: [`docs/captcha-taxa-de-acerto.md`](docs/captcha-taxa-de-acerto.md).
Em consulta real, o objeto de teste foi resolvido na 1ª tentativa numa execução e na 2ª em outra.

### Como a robustez é obtida (e o que ela custa)

Com acerto `p` por imagem e `N` tentativas, a chance de resolver é `1 − (1 − p)^N`
(padrão `N = 15`). `tentativasCaptcha` no JSON mostra o que ocorreu. Leituras vazias ou abaixo da
`ConfiancaMinima` são descartadas **sem submeter** — economizando requisições, que são o recurso
escasso por causa do rate limit.

### Limitações do reconhecimento

- **Não é 100%.** Erros típicos: letras coladas/sobrepostas e pares visualmente próximos
  (`i`/`l`, `u`/`v`, `n`/`m`). Quando erra, o portal recusa e o laço tenta outra imagem.
- O modelo foi treinado com uma amostra **limitada** de imagens reais (coleta lenta por causa do
  rate limit). Mais dados rotulados melhorariam o acerto — o pipeline de treino está pronto.
- Se o portal trocar de fonte, cores ou distorção do CAPTCHA, o modelo precisa ser retreinado
  (e a limpeza por cor, reajustada).

---

## Tratamento de erros

Nenhuma falha de negócio é exceção para o chamador: o resultado traz `sucesso=false` e `erro`.

| `erro.codigo` | Quando | Repete? |
|---|---|---|
| `codigoInvalido` | Formato diferente de `AA123456789BR` (respondido **sem abrir navegador**) | não |
| `objetoNaoEncontrado` | CAPTCHA aceito, mas o portal recusou o objeto (ele responde "Período inválido" para códigos inexistentes) | não |
| `captchaNaoResolvido` | Esgotou `MaxTentativas` sem o portal aceitar | não |
| `portalIndisponivel` | Portal fora, página não abriu, **bloqueio 429** | sim, com backoff |
| `timeout` | Página ou resposta excedeu o tempo configurado | sim, com backoff |
| `layoutAlterado` | Elemento esperado não existe / HTML sem eventos | não |
| `erroInesperado` | Qualquer outra exceção (capturada na fronteira) | não |

Além disso: navegador sempre fechado (`finally`), cancelamento com Ctrl+C, logs em três destinos
(console, texto, JSON estruturado com `ExecucaoId`), e a informação de "ausência de dados" é
tratada (data `00/00/0000` vira `null`).

---

## Desafio Extra — o que foi feito

| Item | Onde |
|---|---|
| Múltiplos objetos em uma execução | `ConsultarVariosAsync`, CLI `códigos…`/`--arquivo` |
| Exportação para `.json` | `--saida`, `ExportadorJson` |
| Logs da execução | Serilog: console, `logs/netspeed-*.log`, `logs/netspeed-estruturado-*.json` |
| Retries estruturados | `PoliticaDeRetry` (backoff exponencial, só para falhas transitórias) |
| Configuração de timeout | `AppSettings:Correios` |
| Testes automatizados | `tests/NetSpeed.Tests` |
| Navegação separada da extração | `IPaginaRastreamento` × `RastroHtmlParser` |
| API para as consultas | `NetSpeed.Web` |
| Interface simples | `NetSpeed.Web/wwwroot/index.html` |
| Execução assíncrona | fila de jobs + `BackgroundService` (POST → 202 → GET) |
| Dependency Injection | `NetSpeed.DependencyInjection` |
| Outras | CAPTCHA isolado atrás de `ICaptchaSolver`; confiança mínima; ferramenta de calibração; pipeline de treino do modelo |

---

## Estrutura do repositório

```
NetSpeed-Teste-Glauber/
├── README.md · docs/decisoes-tecnicas.md
├── Directory.Build.props            configuração comum (.NET 8, avisos = erros)
├── NetSpeed.Rastreamento.sln
├── modelos/captcha-crnn.onnx        rede neural do CAPTCHA
├── tessdata/eng.traineddata         dados do Tesseract (fallback)
├── exemplos/                        JSON de exemplo
├── src/
│   ├── NetSpeed.Domain              modelos, configuração, exceções e PORTAS (interfaces)
│   ├── NetSpeed.Captcha             pré-processamento, solvers (ONNX/Tesseract) e laço de tentativas
│   ├── NetSpeed.Infrastructure      Selenium (PaginaCorreios), parser HTML, exportação JSON
│   ├── NetSpeed.Pipelines           pipes, start pipe, PipelineController/PipelineComum
│   ├── NetSpeed.Application         RastreamentoService, PoliticaDeRetry
│   ├── NetSpeed.DependencyInjection composição (AddNetSpeed) e Serilog
│   ├── NetSpeed.Executor            host de linha de comando
│   └── NetSpeed.Web                 host HTTP (API + interface)
├── tests/NetSpeed.Tests             xUnit
└── tools/
    ├── NetSpeed.CaptchaCalibrador   mede a taxa de acerto contra o portal; gera tensores
    └── treinamento/                 Python: dados sintéticos, treino, exportação ONNX
```

Dependências entre camadas: `Domain` ← `Captcha`/`Infrastructure` ← `Pipelines` ← `Application`
← `DependencyInjection` ← hosts. Só a `DependencyInjection` conhece implementações concretas.

---

## Decisões técnicas

Resumo — o detalhamento está em [`docs/decisoes-tecnicas.md`](docs/decisoes-tecnicas.md):

1. **Selenium** (padrão da base de automações do time) atrás de `IPaginaRastreamento`.
2. **Navegação × extração** separadas: o parser só conhece HTML e é testado com fixtures.
3. **CAPTCHA** = limpeza por cor + rede neural treinada; Tesseract só de fallback.
4. **Robustez do CAPTCHA** vem do laço de tentativas com o portal como oráculo.
5. **Rate limit do portal** moldou paralelismo, retry e descarte de leituras duvidosas.
6. **Pipelining + start pipe** (mesma estrutura do RPA de produção).
7. **Erros como valor** na borda; exceção só para o retry das falhas transitórias.
8. **Uma sessão de navegador por consulta** (isolamento).

---

## Limitações conhecidas

- **Rate limit do portal (HTTP 429).** Depois de algumas dezenas de requisições em poucos minutos o
  portal bloqueia o IP por um tempo. O robô detecta, classifica como `portalIndisponivel` e
  repete com *backoff*, mas **não há como contorná-lo** — nem deveria: o lote é sequencial e
  espaçado (`Lote:MaxParalelismo = 1`, `IntervaloEntreConsultasMs`). Para volume real seria preciso
  combinar com os Correios (ou usar a API oficial contratada).
- **Taxa de acerto do CAPTCHA < 100%** (ver a tabela). O laço de tentativas compensa ao custo de
  requisições.
- **Só código de objeto** (`AA123456789BR`). CPF/CNPJ exigem login e ficam fora do escopo.
- **Windows x64** por causa do binário nativo do Tesseract; sem ele a rede neural funciona sozinha
  (multiplataforma) e a aplicação segue, com aviso no log.
- **Dependência do layout** do portal (ids e classes CSS). Mudanças viram `layoutAlterado` com
  mensagem clara, mas exigem ajuste no parser/página.
- Jobs da API ficam **em memória** (reiniciar o processo os perde).
- O Selenium Manager precisa de internet na primeira execução para obter o *chromedriver*.

---

## Melhorias futuras

- Mais imagens reais rotuladas (via `tools/`) para subir o acerto; *test-time augmentation*.
- Cache/reuso de sessão de navegador quando o portal permitir, para reduzir requisições.
- Fila persistente (banco/Redis) e *rate limiter* compartilhado para a API.
- Métricas (Prometheus) de taxa de acerto do CAPTCHA por dia — alerta de deriva quando o portal
  mudar a imagem.
- Suporte a CefSharp como implementação alternativa de `IPaginaRastreamento`.
- Validação do dígito verificador do código de rastreamento antes de consultar.

---

## Licenças de terceiros

`tessdata/eng.traineddata` é do projeto Tesseract (Apache-2.0, `tessdata_fast`). Bibliotecas via NuGet
mantêm suas licenças. As imagens em `tools/treinamento/dados/brutas` são amostras do CAPTCHA público
do portal, usadas apenas para calibrar o reconhecedor.
