/**
 * Kodet e llogarive — PLACEHOLDER simbolik për walking skeleton.
 * Plani real kontabël vjen nga `docs/canonical/02-accounting/Accounting_Posting_Rules_v0`.
 * I izoluar këtu që zëvendësimi të mos prekë posting rules.
 */
export const ACCOUNTS = {
  AR: "1210", // Klientë (Accounts Receivable)
  AP: "4010", // Furnitorë (Accounts Payable)
  AP_FARMER: "4015", // Fermerë (AP)
  REVENUE: "6010", // Të ardhura nga shitja
  EXPENSE: "5010", // Blerje / shpenzim / stok
  VAT_OUTPUT: "4710", // TVSH e llogaritur (output)
  VAT_INPUT: "4720", // TVSH e zbritshme (input)
  VAT_FARMER_FLAT_PENDING: "4725", // TVSH flat fermer — pending sign-off (ACC-006)
} as const;
