PROJETO VERSÃO DO TEMPLATE **DESAFIO DESENVOLVIMENTO – AUTOMAÇÕES WEB/RPA 1.1** RESUMO DO ASSUNTO Projeto para Processo Seletivo de Candidatos – Desenvolvedor de Automações WWW.NETSPEED.COM.BRWeb/RPA

# Processo de recrutamento

Olá candidato, pronto para participar do nosso processo de recrutamento para vaga de **Desenvolvedor de Automações Web/RPA**?

# Sobre a Vaga

- 100% Remota
- Escritório fica em: São José do Rio Preto-SP
- Empresa: <u>[https://netspeed.com.br/](https://netspeed.com.br/)</u>
O Desenvolvedor de Automações Web/RPA irá atuar na criação, manutenção e evolução de soluções de automação de processos, principalmente envolvendo aplicações Web, portais governamentais e outros sistemas externos.

O profissional deverá possuir conhecimentos de desenvolvimento de software e tecnologias Web, sendo capaz de analisar diferentes cenários e selecionar a abordagem e as tecnologias mais adequadas para cada automação.

# Requisitos para a vaga

Bons conhecimentos em:

- Lógica de programação e desenvolvimento de software;
- Desenvolvimento Web;
- HTML e DOM;
- JavaScript;
- HTTP/HTTPS;
- C# e plataforma .NET;
- Automação de navegadores e processos Web;
- APIs REST e integração entre sistemas;
- Tratamento de erros, depuração e análise de logs
© NETSPEED 2026

WWW.NETSPEED.COM.BR

Desejável:

- CefSharp;
- Playwright;
- Selenium;
- Puppeteer;
- Python aplicado à automação;
- Node.js / TypeScript;
- Web scraping;
- Sistemas de Controle de Versão — Git e/ou SVN;
- Boas práticas de programação.
# O Desafio

O objetivo deste desafio é desenvolver uma solução capaz de consultar o **rastreamento de objetos** **no portal dos Correios** e transformar todas as informações disponíveis no histórico de rastreamento em uma estrutura JSON.

Portal a ser utilizado: <u>[https://rastreamento.correios.com.br/app/index.php](https://rastreamento.correios.com.br/app/index.php)</u>

Para os testes iniciais poderá ser utilizado o seguinte código de rastreamento: **NN437873753BR**

A solução deverá receber o **código de rastreamento como parâmetro**, realizar a consulta correspondente e obter **todos os eventos disponíveis no histórico de rastreamento do objeto**.

O resultado deverá ser apresentado em **formato JSON estruturado**.

© NETSPEED 2026

WWW.NETSPEED.COM.BR

## Conhecendo o cenário

O portal de rastreamento dos Correios apresenta, para cada objeto, uma sequência cronológica de eventos.

Dependendo do evento, poderão existir diferentes tipos de informação, como:

- situação ou status;
- descrição complementar;
- data;
- horário;
- local;
- unidade de origem;
- unidade de destino;
- mensagens adicionais relacionadas ao evento.
Nem todos os eventos possuem necessariamente o mesmo conjunto de informações.

A solução deverá ser capaz de interpretar essas diferenças e estruturar adequadamente os dados retornados.

# O que esperamos?

A solução deverá contemplar os seguintes itens.

## 1. Código de rastreamento parametrizado

O código de rastreamento deverá ser recebido como parâmetro. O valor utilizado para testes não deverá ficar fixo no código-fonte da aplicação.

Exemplo:

NN437873753BR

## 2. Acesso ao portal de rastreamento

A solução deverá acessar o portal de rastreamento dos Correios e realizar o fluxo necessário para consultar o código informado.

© NETSPEED 2026

WWW.NETSPEED.COM.BR

## 3. Identificação das informações da página

A automação deverá identificar e interpretar os elementos apresentados pelo portal, obtendo as informações relacionadas ao objeto consultado.

Esperamos que o candidato demonstre conhecimentos de:

- navegação Web;
- HTML;
- DOM;
- JavaScript;
- interação programática com páginas Web;
- identificação e manipulação de elementos;
- extração de informações estruturadas.
## 4. Obtenção de todos os eventos

A solução deverá obter **todo o histórico disponibilizado para o objeto**, e não apenas o evento mais recente.

Cada evento deverá conter todas as informações relevantes que estiverem disponíveis na página.

Entre elas poderão estar:

- status;
- descrição;
- data e hora;
- local;
- origem;
- destino;
- demais informações apresentadas pelo portal.
## 5. Estruturação em JSON

O histórico deverá ser convertido para um formato JSON estruturado.

A modelagem do JSON fica a critério do candidato.

© NETSPEED 2026

|||WWW.NETSPEED.COM.BR Como referência, poderá ser utilizada uma estrutura semelhante a:|
|---|---|---|
|{|{ {|"codigoObjeto": "NN437873753BR", "eventos": [ "status": "Objeto entregue ao destinatário", "descricao": null, "dataHora": "2026-08-31T16:04:00", "local": "São José do Rio Preto-SP", "origem": null, "destino": null }, "status": "Objeto em transferência-por favor aguarde", "descricao": null, "dataHora": "2026-08-28T08:52:00", "local": null, "origem": "Unidade de Tratamento, São José do Rio Preto-SP", "destino": "Unidade de Distribuição, São José do Rio Preto-SP"|
||}||
||]||
|}|• • • • • • • • • • • •|Essa estrutura é apenas uma referência. O candidato poderá propor outra modelagem, desde que: as informações estejam organizadas; todos os eventos sejam preservados; os dados sejam facilmente interpretáveis; as diferenças entre os tipos de evento sejam representadas adequadamente. 6. Tratamento de situações inesperadas A solução deverá prever minimamente situações como: código de rastreamento inválido; objeto não encontrado; falha no carregamento da página; indisponibilidade temporária do portal; ausência de informações esperadas; alteração de elementos da página; timeout; falhas durante a navegação ou processamento. © NETSPEED 2026|

WWW.NETSPEED.COM.BR

# Tecnologia

A tecnologia utilizada fica a critério do candidato.

Temos preferência por soluções desenvolvidas utilizando: **C# + JavaScript**

Preferencialmente utilizando: **CefSharp / Chromium**

Essa preferência existe por proximidade com algumas das tecnologias utilizadas atualmente em nossas soluções de automação.

Entretanto, o candidato poderá utilizar outras tecnologias ou frameworks, como:

- Playwright;
- Selenium;
- Puppeteer;
- Python;
- Node.js;
- TypeScript;
- ou outra abordagem que considere tecnicamente adequada.
A escolha da tecnologia deverá ser explicada pelo candidato.

Não esperamos que todas as novas automações sejam necessariamente desenvolvidas utilizando a mesma tecnologia.

# Mecanismos de proteção do portal

A solução deverá executar todo o fluxo de forma automatizada, sem intervenção manual do usuário.

Como parte do desafio, será fornecido um mecanismo de CAPTCHA baseado em imagem, semelhante aos encontrados em portais externos.

© NETSPEED 2026

WWW.NETSPEED.COM.BR

A solução deverá:

- obter a imagem do CAPTCHA;
- processar a imagem programaticamente;
- identificar os caracteres apresentados;
- preencher automaticamente o valor identificado;
- validar o resultado;
- tratar falhas de reconhecimento;
- solicitar uma nova imagem e realizar nova tentativa quando necessário;
- registrar as tentativas realizadas e seus resultados.
A estratégia e as tecnologias utilizadas ficam a critério do candidato.

A resolução automática do CAPTCHA faz parte da avaliação técnica do desafio.

# Desafio Extra (opcional)

Como diferenciais, o candidato poderá implementar uma ou mais das seguintes funcionalidades:

- consulta de múltiplos objetos em uma única execução;
- exportação do resultado para arquivo .json;
- implementação de logs da execução;
- tratamento estruturado de retries;
- configuração de timeout;
- testes automatizados;
- separação da lógica de navegação da lógica de extração dos dados;
- criação de API para execução das consultas;
- criação de interface simples para informar o código de rastreamento;
- execução assíncrona;
- utilização de Dependency Injection;
- implementação de outras melhorias consideradas relevantes.
© NETSPEED 2026

WWW.NETSPEED.COM.BR

# Avaliação

O que vamos avaliar:

- lógica e capacidade de resolução do problema;
- qualidade, organização e padronização do código;
- domínio da linguagem e das tecnologias utilizadas;
- domínio de desenvolvimento Web;
- conhecimento de HTML, DOM e JavaScript;
- capacidade de identificar e manipular elementos Web;
- capacidade de automação de navegador;
- capacidade de automatizar o fluxo integralmente;
- extração estruturada das informações;
- qualidade da modelagem do JSON;
- estratégia utilizada para processamento da imagem do CAPTCHA;
- robustez do reconhecimento do CAPTCHA;
- separação da lógica de resolução do CAPTCHA do restante da automação;
- tratamento de erros, situações inesperadas e novas tentativas;
- manutenibilidade e extensibilidade da solução;
- aplicação de boas práticas de desenvolvimento;
- capacidade de investigação e diagnóstico de problemas;
- clareza da implementação e das decisões técnicas;
- capacidade de justificar as tecnologias e abordagens escolhidas;
- capacidade de explicar as limitações da solução, incluindo a taxa de acerto do reconhecimento do CAPTCHA.
Não será avaliada apenas a quantidade de funcionalidades implementadas.

Serão consideradas principalmente a **qualidade da solução, a organização do código, a robustez** **da automação e as decisões técnicas adotadas**.

# Entrega

A solução deverá ser enviada preferencialmente através de um **repositório Git**.

O repositório deverá conter:

- código-fonte da solução;
© NETSPEED 2026

WWW.NETSPEED.COM.BR

- arquivos necessários para execução;
- dependências utilizadas;
- instruções para configuração do ambiente;
- instruções para execução;
- exemplo de utilização;
- exemplo do JSON gerado.
Também deverá existir um arquivo README contendo uma breve descrição da solução.

No README, esperamos encontrar:

- tecnologia utilizada;
- bibliotecas e frameworks adotados;
- estratégia utilizada para realizar a automação;
- forma utilizada para localizar e interpretar os elementos da página;
- estratégia utilizada para transformar as informações em JSON;
- tratamento de erros adotado;
- limitações conhecidas;
- decisões técnicas relevantes;
- possíveis melhorias futuras.
A solução poderá ser enviada por e-mail ou através do link para o repositório utilizado.

© NETSPEED 2026