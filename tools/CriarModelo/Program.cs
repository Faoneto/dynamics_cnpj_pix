// Capítulo 1 · Modelo de dados (versão que pode rodar quantas vezes quiser)
// O que já existe no Dataverse ele PULA; o que falta ele CRIA.
// Se der erro no meio, corrija e rode de novo: ele continua de onde parou.
//
// Mudança de arquitetura: o ambiente não tem Dynamics 365 Sales (sem a tabela
// Oportunidade), então a cobrança nasce de uma tabela própria "Venda" (fan_venda).
// Num ambiente com Sales, esse papel seria da Oportunidade.

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

// ====== AJUSTE ESTES 3 VALORES ======
const string Url = "https://org637d4c7a.crm2.dynamics.com/";   // URL do seu ambiente Developer
const int Idioma = 1046;          // idioma BASE do ambiente: 1046 = pt-BR, 1033 = inglês
const int PrefixoOpcao = 79363;   // prefixo de valor de opção do publisher "fan"
// ====================================

const string Solucao = "CnpjPix";
const string Venda = "fan_venda";
const string Cobranca = "fan_cobrancapix";

var conexao = $"AuthType=OAuth;Url={Url};" +
              "Username=FRANCISCOON@wnx3m.onmicrosoft.com;" +
              "AppId=51f81489-12ee-4a9e-aaae-a2591f45987d;" +
              "RedirectUri=http://localhost;LoginPrompt=Auto";
using var svc = new ServiceClient(conexao);
if (!svc.IsReady) throw new Exception(svc.LastError);
Console.WriteLine($"Conectado em {svc.ConnectedOrgFriendlyName}\n");

var opcional = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None);

// ---------- Conta ----------
Coluna("account", Texto("fan_CNPJ", "CNPJ", 14));
Coluna("account", Texto("fan_RazaoSocialReceita", "Razão social (Receita)", 200));
Coluna("account", Texto("fan_CNAE", "CNAE principal", 200));
Coluna("account", Data("fan_DataConsulta", "Data da consulta à Receita"));
Coluna("account", Escolha("fan_SituacaoCadastral", "Situação cadastral",
                          "Ativa", "Suspensa", "Inapta", "Baixada", "Nula"));
Chave("account", "fan_ChaveCnpj", "Chave CNPJ", "fan_cnpj");

// ---------- Venda (no lugar da Oportunidade) ----------
Tabela("fan_Venda", "Venda", "Vendas", notas: false);
Coluna(Venda, Moeda("fan_Valor", "Valor da venda"));
Lookup(Venda, "account", "fan_Conta", "Conta");

// ---------- Cobrança PIX ----------
Tabela("fan_CobrancaPix", "Cobrança PIX", "Cobranças PIX", notas: true);
Coluna(Cobranca, Protegida(Moeda("fan_Valor", "Valor")));
Coluna(Cobranca, Protegida(Moeda("fan_ValorPago", "Valor pago")));
Coluna(Cobranca, Texto("fan_TxId", "TxId", 35));
Coluna(Cobranca, Texto("fan_E2eId", "EndToEndId", 32));
Coluna(Cobranca, new MemoAttributeMetadata { SchemaName = "fan_PixCopiaECola", DisplayName = R("PIX copia e cola"),
                                             MaxLength = 1000, RequiredLevel = opcional });
Coluna(Cobranca, Data("fan_Vencimento", "Vencimento"));
Coluna(Cobranca, Protegida(Data("fan_PagoEm", "Pago em")));
var status = Escolha("fan_Status", "Status", "Pendente", "Paga", "Expirada", "Cancelada");
status.DefaultFormValue = PrefixoOpcao * 10000;   // nasce Pendente
Coluna(Cobranca, Protegida(status));

Lookup(Cobranca, "account", "fan_Conta", "Conta");
Lookup(Cobranca, Venda, "fan_Venda", "Venda");
Chave(Cobranca, "fan_ChaveTxId", "Chave TxId", "fan_txid");

Console.WriteLine("\nPronto. Confira a solution CnpjPix no maker portal.");


// ================= fábricas de coluna =================
Label R(string texto) => new Label(texto, Idioma);

StringAttributeMetadata Texto(string schema, string nome, int max) =>
    new() { SchemaName = schema, DisplayName = R(nome), MaxLength = max, RequiredLevel = opcional };

DateTimeAttributeMetadata Data(string schema, string nome) =>
    new(DateTimeFormat.DateAndTime) { SchemaName = schema, DisplayName = R(nome), RequiredLevel = opcional };

MoneyAttributeMetadata Moeda(string schema, string nome) =>
    new() { SchemaName = schema, DisplayName = R(nome), PrecisionSource = 2, RequiredLevel = opcional };

PicklistAttributeMetadata Escolha(string schema, string nome, params string[] opcoes)
{
    var lista = new OptionSetMetadata { IsGlobal = false, OptionSetType = OptionSetType.Picklist };
    for (int i = 0; i < opcoes.Length; i++)
        lista.Options.Add(new OptionMetadata(R(opcoes[i]), PrefixoOpcao * 10000 + i));
    return new() { SchemaName = schema, DisplayName = R(nome), OptionSet = lista, RequiredLevel = opcional };
}

// TCol e não T: um nome T aqui colidiria com outras variáveis do arquivo
TCol Protegida<TCol>(TCol coluna) where TCol : AttributeMetadata { coluna.IsSecured = true; return coluna; }


// ================= criar só o que falta =================
bool Existe(OrganizationRequest consulta)
{
    try { svc.Execute(consulta); return true; }
    catch { return false; }   // "não encontrado" → não existe (se for outro problema, a criação mostra o erro real)
}

void Coluna(string tabela, AttributeMetadata coluna)
{
    var logico = coluna.SchemaName.ToLowerInvariant();
    if (Existe(new RetrieveAttributeRequest { EntityLogicalName = tabela, LogicalName = logico }))
    { Console.WriteLine($"--  {tabela}.{logico} já existe"); return; }

    svc.Execute(new CreateAttributeRequest { EntityName = tabela, Attribute = coluna, SolutionUniqueName = Solucao });
    Console.WriteLine($"ok  {tabela}.{logico}");
}

void Tabela(string schema, string nome, string plural, bool notas)
{
    var logico = schema.ToLowerInvariant();
    if (Existe(new RetrieveEntityRequest { LogicalName = logico, EntityFilters = EntityFilters.Entity }))
    { Console.WriteLine($"--  tabela {logico} já existe"); return; }

    svc.Execute(new CreateEntityRequest
    {
        SolutionUniqueName = Solucao,
        Entity = new EntityMetadata
        {
            SchemaName = schema,
            DisplayName = R(nome), DisplayCollectionName = R(plural),
            OwnershipType = OwnershipTypes.UserOwned,
            HasNotes = notas,
            IsAuditEnabled = new BooleanManagedProperty(true)
        },
        PrimaryAttribute = Texto("fan_Name", "Descrição", 100)
    });
    Console.WriteLine($"ok  tabela {logico}");
}

// filho = o lado "N" (quem ganha a coluna de lookup); pai = o lado "1"
void Lookup(string filho, string pai, string schema, string nome)
{
    var relacao = $"fan_{pai}_{filho}";
    if (Existe(new RetrieveRelationshipRequest { Name = relacao }))
    { Console.WriteLine($"--  relação {relacao} já existe"); return; }

    svc.Execute(new CreateOneToManyRequest
    {
        SolutionUniqueName = Solucao,
        OneToManyRelationship = new OneToManyRelationshipMetadata
        {
            ReferencedEntity = pai, ReferencingEntity = filho, SchemaName = relacao
        },
        Lookup = new LookupAttributeMetadata { SchemaName = schema, DisplayName = R(nome) }
    });
    Console.WriteLine($"ok  relação {relacao}");
}

void Chave(string tabela, string schema, string nome, string coluna)
{
    var logico = schema.ToLowerInvariant();
    try
    {
        var r = (RetrieveEntityKeyResponse)svc.Execute(
            new RetrieveEntityKeyRequest { EntityLogicalName = tabela, LogicalName = logico });
        Console.WriteLine($"--  chave {logico} já existe · status: {r.EntityKeyMetadata.EntityKeyIndexStatus}");
        return;
    }
    catch { /* não existe: cria abaixo */ }

    svc.Execute(new CreateEntityKeyRequest
    {
        EntityName = tabela, SolutionUniqueName = Solucao,
        EntityKey = new EntityKeyMetadata
        {
            SchemaName = schema, DisplayName = R(nome), KeyAttributes = new[] { coluna }
        }
    });
    Console.WriteLine($"ok  chave {logico} (fica Ativa em alguns minutos)");
}
