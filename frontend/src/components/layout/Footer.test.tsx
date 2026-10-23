import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { Footer } from "@/components/layout/Footer";

// Pure presentational component — no providers/wrapper needed. The one behavior worth locking
// down: a real mailto `<a>` (not a styled span/button) with the correct support address.
describe("Footer", () => {
  it("renders a mailto link to the support address with visible link text", () => {
    render(<Footer />);

    const link = screen.getByRole("link", { name: "Contact support" });
    expect(link).toHaveAttribute("href", "mailto:emiliochanderdutt9@gmail.com");
  });

  it("renders the support prompt text", () => {
    render(<Footer />);

    expect(screen.getByText(/need help\?/i)).toBeInTheDocument();
  });
});
