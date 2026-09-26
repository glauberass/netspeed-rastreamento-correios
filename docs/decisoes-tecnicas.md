# Decisões técnicas

Cada decisão abaixo responde a uma pergunta que o avaliador provavelmente faria. Estão em ordem
de importância para o resultado.

## 1. Selenium + Chrome (em vez de CefSharp, Playwright ou HTTP puro)

| Opção | Por que sim / por que não |
|---|---|
| **Selenium (escolhida)** | É o que as automações de produção do time já usam (o repositório de referência é todo Selenium 4 + Chrome). API madura, `IJavaScriptExecutor` para o trecho em JS, Selenium Manager baixa o driver certo sozinho, roda em Windows/Linux/Mac. |
| CefSharp | É a preferência declarada no enunciado e faria sentido embutido numa aplicação WinForms/WPF. Para um robô de linha de comando/API só acrescenta: Windows-only, x86/x64 separados, bootstrap pesado e depuração pior. O ganho — acesso ao DOM in-process — é o que `ExecuteScript` já entrega. |
| Playwright | Excelente (auto-wait), mas exige baixar seus próprios navegadores e o time já tem know-how e infraestrutura em Selenium. Trocar não agrega para este portal. |
| HTTP puro (`HttpClient` em `resultado.php`) | Tecnicamente possível — o portal expõe `resultado.php?objeto=…&captcha=…` —, mas o enunciado pede **navegação e interpretação do DOM**, e a chamada direta contornaria justamente o que se quer avaliar. Foi usada apenas como *ferramenta de calibração* do CAPTCHA (ver `tools/`). |

O acesso ao navegador fica atrás de `IPaginaRastreamento` (Domain). Trocar Selenium por
CefSharp/Playwright é escrever **uma classe** nova; pipeline, CAPTCHA e parser não mudam.

## 2. Navegação separada da extração

`PaginaCorreios` (Selenium) termina entregando **uma string de HTML** (`#tabs-rastreamento`).
`RastroHtmlParser` (HtmlAgilityPack) só conhece HTML. Efeito prático: o parser é testado com
fixtures, sem navegador, sem rede e sem CAPTCHA; e se o layout do portal mudar, o defeito está
isolado em um arquivo pequeno.

## 3. CAPTCHA: separar a limpeza do reconhecimento

O Securimage desenha letras, linhas e ruído em **tons de cinza distintos e sem antialiasing**.
Isolar o tom das letras remove as linhas de uma vez e transforma um problema de visão difícil em um
problema fácil de OCR. (Binarizar por limiar não serve: as linhas são *mais escuras* que as
letras e sobreviveriam ao corte.)

O reconhecimento em si é uma CRNN treinada (ver `tools/treinamento/`). O Tesseract, que foi a
primeira tentativa, ficou como *fallback*: sozinho acertava pouco porque o LSTM genérico dele
não conhece essas letras deformadas. A decisão de treinar um modelo próprio veio **de medir**
esse acerto contra o portal (que é o oráculo: aceita ou recusa o texto).

Por que o solver está em `NetSpeed.Captcha`, sem referência a Selenium: o enunciado avalia a
"separação da lógica de resolução do CAPTCHA do restante da automação". Ele recebe `byte[]` e
devolve `LeituraCaptcha(texto, confiança)`.

## 4. Robustez do CAPTCHA vem do laço de tentativas, não do OCR perfeito

Nenhum reconhecedor acerta 100%. O que torna a consulta confiável é o laço em
`ResolvedorDeCaptchaNoPortal`: lê → submete → se recusado, o portal já renova a imagem → lê de
novo. Com acerto `p` por imagem e `N` tentativas, a chance de resolver é `1 − (1 − p)^N`.
Cada tentativa fica registrada no JSON (`tentativasCaptcha`).

Duas peças evitam gastar requisições à toa (importante — ver item 5):

- **Leitura vazia** (o solver não achou letras) → pede nova imagem sem consultar.
- **Confiança mínima** (`Captcha:ConfiancaMinima`) → leitura duvidosa é descartada sem consultar.

## 5. O portal limita requisições (HTTP 429) — e isso moldou o desenho

Durante o desenvolvimento o portal passou a responder *"We are sorry, but the site has received
too many requests"* (429) para o IP, por dezenas de minutos, depois de poucas dezenas de
requisições em poucos minutos. Consequências assumidas no código:

- `MaxParalelismo` padrão **1** e `IntervaloEntreConsultasMs` entre consultas do lote.
- `PaginaCorreios` reconhece a página de bloqueio e a trata como `PortalIndisponivel` (transitória),
  em vez de esperar um timeout e diagnosticar errado como "layout alterado".
- Retry com **backoff exponencial** (`PoliticaDeRetry`) só para falhas transitórias.
- Cada tentativa de CAPTCHA "custa" requisições, então importa mais **acertar de primeira** do
  que tentar muitas vezes — daí o investimento no reconhecedor e na confiança mínima.

## 6. Pipelining + start pipe (padrão do RPA de produção)

O fluxo é `ConsultarObjetoStartPipe` → sub-pipes (`AbrirPagina`, `QuebrarCaptcha`,
`ExtrairEventos`), registrado em `PipelineController`. O encerramento (fechar o Chrome) está em um
grupo **separado** (`PipelineComum`) executado no `finally` do serviço: o que precisa rodar sempre
não pode estar no pipeline que pode falhar. É a mesma estrutura do `Smarthis`/RPA, o que mantém o
código legível para quem já conhece o restante da base.

## 7. Erros viram valor, não exceção, na borda

`ConsultarAsync` **nunca lança** por falha de negócio: devolve `RastreamentoResultado` com
`sucesso=false` e um `erro` de código estável (`codigoInvalido`, `objetoNaoEncontrado`,
`captchaNaoResolvido`, `portalIndisponivel`, `timeout`, `layoutAlterado`, `erroInesperado`).
Em lote, um código com problema não derruba os outros. Falhas *transitórias* sobem como exceção
dentro do pipeline só para a política de retry vê-las, e são convertidas em resultado no final.

## 8. Uma sessão de navegador por consulta

Cookie de sessão e CAPTCHA são vinculados; reaproveitar o navegador entre consultas economizaria
tempo, mas uma sessão "suja" (CAPTCHA em estado estranho, bloqueio parcial) contaminaria as
seguintes. Para robustez, cada tentativa abre e fecha o seu Chrome.

## 9. JSON

- Um objeto por consulta; array quando há vários (o consumidor de um não muda para consumir vários).
- Campos ausentes = `null` (esquema estável), acentos legíveis, enums em texto, data em ISO-8601.
- `origem`/`destino` só em eventos de transferência; `local` só nos demais; o que não se encaixa vai
  em `informacoesAdicionais` — nada é descartado.
- `dataHoraOriginal` preserva o texto do portal, útil para auditoria e para datas inválidas
  (`00/00/0000 00:00` vira `dataHora: null`).
