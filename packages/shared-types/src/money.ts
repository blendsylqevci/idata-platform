import { Decimal } from "decimal.js";

// Konfigurim për siguri financiare: precizion i lartë, rrumbullakim bankar (HALF_EVEN).
Decimal.set({ precision: 40, rounding: Decimal.ROUND_HALF_EVEN });

export type Currency = string; // ISO-4217 (default EUR)

/**
 * `Money` — vlerë monetare e saktë (decimal), KURRË `float`/JS `number`.
 *
 * Invarianti no-float (ARCHITECTURE.md §2): asnjë operacion monetar s'kalon nëpër
 * `number`. Factory-t pranojnë vetëm `string` (dhjetor ekzakt) ose `bigint`.
 * SEF money-scale trajtohet ndaras nga ky numeric i brendshëm i GL-së.
 */
export class Money {
  private constructor(
    private readonly value: Decimal,
    public readonly currency: Currency,
  ) {}

  /** Krijo nga string ekzakt ("100.00") ose bigint. NUK pranon `number`. */
  static of(amount: string | bigint, currency: Currency = "EUR"): Money {
    const d = new Decimal(typeof amount === "bigint" ? amount.toString() : amount);
    if (!d.isFinite()) {
      throw new Error(`Vlerë jo-finite për Money: ${String(amount)}`);
    }
    return new Money(d, currency);
  }

  static zero(currency: Currency = "EUR"): Money {
    return new Money(new Decimal(0), currency);
  }

  private assertSameCurrency(other: Money): void {
    if (this.currency !== other.currency) {
      throw new Error(`Mospërputhje monedhe: ${this.currency} vs ${other.currency}`);
    }
  }

  add(other: Money): Money {
    this.assertSameCurrency(other);
    return new Money(this.value.plus(other.value), this.currency);
  }

  sub(other: Money): Money {
    this.assertSameCurrency(other);
    return new Money(this.value.minus(other.value), this.currency);
  }

  /** Shumëzim me faktor pa njësi (normë TVSH-je, sasi). Faktori string/bigint/Decimal — jo number. */
  mul(factor: string | bigint | Decimal): Money {
    const f = factor instanceof Decimal ? factor : new Decimal(factor.toString());
    return new Money(this.value.times(f), this.currency);
  }

  negate(): Money {
    return new Money(this.value.negated(), this.currency);
  }

  /** Rrumbullakos në `dp` shifra dhjetore (default 2 = cent), HALF_EVEN. */
  round(dp = 2): Money {
    return new Money(this.value.toDecimalPlaces(dp, Decimal.ROUND_HALF_EVEN), this.currency);
  }

  isZero(): boolean {
    return this.value.isZero();
  }

  isNegative(): boolean {
    return this.value.isNegative();
  }

  equals(other: Money): boolean {
    return this.currency === other.currency && this.value.equals(other.value);
  }

  /** -1 nëse < other, 0 nëse ==, 1 nëse >. Kërkon të njëjtën monedhë. */
  compare(other: Money): -1 | 0 | 1 {
    this.assertSameCurrency(other);
    return this.value.comparedTo(other.value) as -1 | 0 | 1;
  }

  /** String ekzakt me `dp` shifra (default 2). */
  toString(dp = 2): string {
    return this.value.toFixed(dp);
  }

  toJSON(): { amount: string; currency: Currency } {
    return { amount: this.value.toFixed(), currency: this.currency };
  }
}
