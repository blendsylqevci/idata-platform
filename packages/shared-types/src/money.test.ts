import { describe, it, expect } from "vitest";
import { Money } from "./money.js";

describe("Money — invarianti no-float", () => {
  it("0.1 + 0.2 == 0.3 ekzakt (float do dështonte)", () => {
    expect(Money.of("0.1").add(Money.of("0.2")).equals(Money.of("0.3"))).toBe(true);
  });

  it("TVSH 18% mbi 100.00 = 18.00 ekzakt", () => {
    expect(Money.of("100.00").mul("0.18").round(2).toString()).toBe("18.00");
  });

  it("rrumbullakim bankar HALF_EVEN", () => {
    expect(Money.of("2.345").round(2).toString()).toBe("2.34"); // shifra para = 4 (çift)
    expect(Money.of("2.355").round(2).toString()).toBe("2.36"); // shifra para = 5 (tek) -> lart
  });

  it("add + sub ruajnë saktësinë", () => {
    const sum = Money.of("0.10").add(Money.of("0.20")).add(Money.of("0.30"));
    expect(sum.toString()).toBe("0.60");
    expect(Money.of("1.00").sub(Money.of("0.55")).toString()).toBe("0.45");
  });

  it("hedh gabim për mospërputhje monedhe", () => {
    expect(() => Money.of("1", "EUR").add(Money.of("1", "USD"))).toThrow();
  });

  it("krijim nga bigint përmes string ekzakt", () => {
    expect(Money.of(100n).toString()).toBe("100.00");
  });

  it("negate + isNegative + compare", () => {
    expect(Money.of("5").negate().isNegative()).toBe(true);
    expect(Money.of("5").compare(Money.of("3"))).toBe(1);
    expect(Money.of("3").compare(Money.of("3"))).toBe(0);
  });

  it("zero + isZero", () => {
    expect(Money.zero().isZero()).toBe(true);
    expect(Money.of("0.00").isZero()).toBe(true);
  });
});
