# CAPTCHA — como o acerto foi medido e o que os números significam

## Como se mede

O portal é o **oráculo**: só ele sabe se um texto está certo, e só descobrimos submetendo. Por isso
existem dois tipos de medida, com significados diferentes:

| Medida | Como | Para que serve | Viés |
|---|---|---|---|
| **Ao vivo (a que vale)** | `NetSpeed.CaptchaCalibrador live N`: busca uma imagem nova, lê, submete em `resultado.php` e vê se o portal aceitou | Taxa de acerto **real** por imagem, na distribuição real do portal | Nenhum (só a incerteza estatística de N pequeno) |
| Offline, contra rótulos | `NetSpeed.CaptchaCalibrador avaliar <pasta> <rotulos.csv>` | Comparar modelos rapidamente, sem tocar no portal | **Otimista**: só entram imagens que eu consegui rotular com segurança; as ilegíveis/ambíguas ficaram de fora |

O ritmo do `live` é lento de propósito (12 s entre tentativas): o portal bloqueia o IP (HTTP 429) com
poucas dezenas de requisições em poucos minutos. Por isso as amostras ao vivo são **pequenas** e os
intervalos de confiança, largos — abaixo, tudo vem com intervalo de Wilson de 95%.

## Resultados

### Linha do tempo (o que foi tentado e o que mediu)

| # | Solver | Onde | Resultado |
|---|---|---|---|
| 1 | Tesseract, 1ª versão do pré-processamento | ao vivo, 21 leituras | 2 aceitas ≈ **10%** |
| 2 | Tesseract, pré-processamento final | offline, 148 imagens rotuladas | **14,2%** do texto inteiro (44,9% dos caracteres) |
| 3 | Rede neural treinada **só com sintéticos** (v1) | **ao vivo, 40 tentativas** | **25 aceitas = 62,5%** (IC 95%: 47%–76%) |
| 4 | v1 | offline, 148 rotuladas (que ela nunca viu) | 75,7% do texto inteiro (90,0% dos caracteres) |
| 5 | v1 vs. v2 (ajuste fino com imagens reais) | 24 imagens reais **retidas** do treino | 58,3% → **75,0%** |
| 6 | Rede neural final (v2, ajuste fino com 148 imagens reais) | **ao vivo** | **29 aceitas em 40 = 72,5%** (IC 95%: 57%–84%); confiança ≥95%: 27/33 = 82% |

Observações:

- A linha 3 (ao vivo, 62,5%) e a linha 5 (v1 = 58,3% nas retidas) concordam entre si; a linha 4
  (75,7%) é maior porque o conjunto rotulado **exclui** as imagens que eu não soube ler — é o viés
  otimista descrito acima. **Vale a medida ao vivo.**
- O Tesseract, com o mesmo pré-processamento, acerta ~1 imagem em 7: o modelo de linguagem
  genérico dele não conhece estas letras deformadas. Ficou como *fallback*.
- Rede treinada só com sintéticos já transfere bem para o portal (o gerador imita a fonte, as
  deformações e os defeitos da limpeza); o ajuste fino com poucas imagens reais fecha parte da
  diferença.

### A confiança da rede é informativa (v1, ao vivo, 40 tentativas)

| Faixa de confiança | Aceitas | Taxa |
|---|---|---|
| < 60% | 0/3 | 0% |
| 80–90% | 1/6 | 17% |
| 90–95% | 5/10 | 50% |
| ≥ 95% | 19/21 | 90% |

Descartar leituras abaixo de um limiar (`Captcha:ConfiancaMinima`) evita submeter palpites ruins.
Atenção ao custo: descartar uma imagem também custa uma requisição (a nova imagem), então o limiar
melhora a **qualidade** das submissões, mas **não reduz o número de requisições por sucesso** — por
isso o padrão é `0` (submeter sempre) e o limiar fica como opção de ajuste.

## O que isso significa na prática

Com acerto `p` por imagem e `N = 15` tentativas por consulta, a chance de **não** resolver é
`(1 − p)^N`. Com `p = 72,5%`, isso dá ≈ `0,275^15` ≈ 3·10⁻⁹; o número esperado de imagens até acertar
é `1/p` ≈ 1,4. Na prática, o que limita a vazão **não é o acerto do OCR, e sim o rate limit do portal**
(cada tentativa custa ~2 requisições, e o portal bloqueia poucas dezenas em poucos minutos).

## Limitações destes números

- **Amostra ao vivo pequena** (dezenas de tentativas por medida) por causa do rate limit; os
  intervalos de confiança são largos (± ~14 pontos).
- **Rótulos manuais** das imagens de treino/validação têm ruído (i/l, g/9, u/v). Imagens em que
  o portal recusou a leitura do modelo foram rotuladas por inspeção visual, sem confirmação.
- O portal pode trocar fonte/cores/distorção a qualquer momento; o modelo então precisa de
  retreino (`tools/treinamento`) e a limpeza por cor, de reajuste.
