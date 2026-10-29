# Bonsaimancer Business Setup

Last updated 2026-10-29. Everything about the business side of getting Bonsaimancer onto Steam:
the LLC, banking, Steamworks, the name and the money. The game-side store and technical work also
lives in `PLAN.md` (R0, R5b, R6–R8).

This is general information, not legal or tax advice. Valve changes its fees and timelines now and
then, so check the Steamworks pages as you go.

## Where things stand

**Done**

- [x] LLC formed and accepted by the state
- [x] EIN from the IRS
- [x] Trademark search: "Bonsaimancer" is clear
- [x] Domain bonsaimancer.com owned

**Next**

- [ ] Find the Articles of Organization. Note the exact legal name, state and filing date.
- [ ] Find the EIN letter (CP 575). If it's lost, call the IRS business line (800-829-4933) for a
      replacement "147C" letter.
- [ ] Decide who owns the IP and the computer, then fill in and sign the operating agreement.
- [ ] Check the bank's business-account checklist and fees.
- [ ] Open the business bank account (planned for 2026-10-30).
- [ ] Create the new business Steam account with Steam Guard.
- [ ] Start the Steamworks signup. Finish the bank and tax steps after the account opens.
- [ ] Pay the 100 USD Steam Direct fee (starts the 30-day clock).
- [ ] Look up the state's annual report deadline and fee, and put it in the calendar.
- [ ] Build the financials spreadsheet and log past game expenses.
- [ ] Set the company name in Unity (PLAN R0), copying existing saves across.

## The road to Steam

Do these roughly in order. The early steps have waiting periods built in, so starting them matters
more than polish.

1. **Finish the LLC setup**
   1. Get an EIN, free on irs.gov. Avoid the paid third-party sites. Keep the CP 575 letter.
   2. Write and sign a simple operating agreement, even if the state doesn't require one. Banks
      often ask for it.
   3. Open a business bank account in the LLC's exact legal name. Valve pays into it, so the
      names must match exactly.
   4. Calendar the state's annual report or franchise tax deadline. Some states charge a lot
      (California is 800 USD a year), and missing one can get the LLC dissolved.
   5. Keep business money separate. Pay for game things from the business account, and log
      income and spending in the spreadsheet.
   6. BOI report: since early 2025, LLCs formed in the US don't have to file one.
   7. Worth an hour with an accountant at some point: how the LLC is taxed. A one-person LLC is a
      "disregarded entity" by default, which affects Valve's tax form.
2. **Steamworks partner account**
   1. Sign up at partner.steamgames.com with the business Steam account.
   2. Enter the LLC's legal name and address, and sign the Steam Distribution Agreement online.
   3. Pay the 100 USD Steam Direct fee. It comes back once the game earns 1,000 USD. Pay it early:
      it starts a 30-day wait before a first release, and nothing has to be ready yet.
   4. Bank details and the tax interview. As a US company you fill in a W-9, using the EIN.
   5. Identity verification, which can take a few days.
   6. You then get an **App ID** for Bonsaimancer. Everything technical hangs off it.
3. **Lock in the name**
   1. Trademark search: done, clear. See the Trademark section below for registering.
   2. The developer name shown on the Steam page can differ from the LLC's legal name.
   3. Set the company name in Unity Player Settings (PLAN R0) before any build goes out. It sets
      the folder saves live in, so it should never change after that.
4. **Store page (PLAN R6; start from `STORE_PAGE_DRAFT.md`)**
   1. Content survey. It gives the age rating through IARC (free, built into Steam). It also asks
      about AI-generated content. Valve's guidance is about content players see (art, text,
      audio), not tools like code assistants. Check the wording on the form.
   2. Assets:
      - capsule images in Steam's required sizes;
      - at least 5 screenshots;
      - a trailer;
      - the short and long descriptions.

      The Store art set tool (Settings ▸ Debug) renders screenshots at Steam's sizes. The capsules
      still need the logo added by hand.
   3. Submit for review (a few business days), then go live as "Coming Soon" to collect wishlists.
   4. The page must be public for at least 2 weeks before release.
5. **Technical (PLAN R8 and R5b)**
   1. Steamworks.NET in Unity: achievements and cloud saves.
   2. Builds uploaded with SteamCMD, scripted once.
   3. A build review before launch, also a few days.
   4. Second monitor, focus handling, and Windows, macOS and Linux builds.
6. **Getting players**
   1. A demo, then Steam Next Fest (you sign up months ahead), or a Steam Playtest.
   2. Pick a release date.

## Key documents

| Document | What it does | Who gives it to you |
|---|---|---|
| Articles of Organization | Creates the LLC. Some states call it a "Certificate of Formation" or "Certificate of Organization". | The state, when it accepted your filing |
| EIN letter (CP 575) | The LLC's tax ID number, like a Social Security number for the business | The IRS |
| Operating agreement | Your own rules for how the LLC runs | You write and sign it |
| Business bank account | Keeps the LLC's money separate from yours | The bank, once you show them the three above |

**Finding the Articles:** look in your email for the approval from the state, or from the filing
service if you used one (LegalZoom, ZenBusiness, Northwest and similar). If it's not there, search
the state Secretary of State's "business search" for the LLC's name. Most states let you download
the filed document for free or a few dollars.

**For the bank, bring:**
- the Articles;
- the EIN letter;
- the signed operating agreement;
- photo ID, sometimes two;
- an opening deposit.

Compare monthly fees and minimum balances; many banks have a free basic business checking account.

## Operating agreement

The draft is at https://claude.ai/artifact/DbFySLRdjmyAZ5Z2LJd6xb. It's a single-member,
member-managed LLC agreement. To finish it:

- Fill in the blanks:
  - the exact LLC name;
  - the state;
  - the filing date;
  - your full legal name and addresses;
  - the effective date;
  - a successor for the membership interest if you die.
- Print it, sign it and keep it with the Articles and the EIN letter.

**Decisions still open**

- **The computer: suggested to keep it out.** Anything you contribute becomes the company's. If the
  company were sued or couldn't pay its debts, its assets could be taken, including a contributed
  computer. Kept as personal property, you can still use it for the business and usually deduct the
  business share (ask an accountant).
- **Who owns the IP (the game, code, art, music, name):**
  - **Company owns it (what most solo indies do).**
    - Steam's agreement has the company promise it holds the rights, which is simplest if it does.
    - Infringement claims go to the company, not you personally.
    - Publishers, partners and buyers expect it.
    - Downside: company creditors could take it.
  - **You own it and license it to the company.**
    - The IP is out of reach of the company's creditors, and you keep it if the LLC closes.
    - Needs a separate written license, or the company can't properly promise Steam it has the
      rights.
    - It also exposes you personally to infringement claims, partly undoing the LLC's protection.
  - **Suggested middle ground:**

    > The game Bonsaimancer, including its code, art, music and name, and all other work the Member
    > creates for the Company's business, belong to the Company. Work the Member creates outside
    > the Company's business remains the Member's own.

  - Owning 100% of the company means the IP is still yours either way, just held through the LLC. It
    can be moved back to you later in a signed document, though that can have tax consequences.

## New Steam account and Steamworks signup

1. Log out of the personal account, or use a private browser window, and go to
   store.steampowered.com/join.
2. Pick a username tied to the business, not a personal gamer name. Valve and partners may see it.
3. Use an email you'll keep long term.
4. Turn on **Steam Guard** with the Steam Mobile app. The app can hold more than one account.
5. Save the login in a password manager. Losing the account that controls the game would be a
   nightmare.
6. Go to partner.steamgames.com signed in as the new account, choose **Join Steamworks**, and fill in
   the company details using the LLC's exact legal name. Stop at bank and tax info until the account
   is open.

## Email and domain

No new email account is needed; the existing inbox is fine. Optional: add a free forwarding address
on bonsaimancer.com, such as `hello@bonsaimancer.com`, that lands in the existing inbox. Most domain
registrars offer forwarding for free. It keeps the music and game business apart in public, and the
forward can be pointed elsewhere later.

## Trademark

The search came back clear: nobody has registered or applied for "Bonsaimancer". That doesn't
protect it yet.

**Without registering:** in the US, trademark rights come from using the name to sell something.
Once the game is on sale you have "common-law" rights. Someone who registers the name after your
release generally can't stop you, and you could challenge their registration because you used it
first.

**Why register anyway:**

- **Proof:** a dispute without a registration means proving your rights (when you started, where
  you sold, how known you are), which takes lawyers. A registration is presumed valid nationwide.
- **Reach:** common-law rights cover where you actually do business and are known. A registration
  covers the whole US automatically.
- **Takedowns:** platform complaint processes for copycat games or scam listings move much faster
  with a registration number.
- **Deadlines:** if someone else registers the name and you don't challenge it within 5 years, your
  position weakens.
- **Other countries:** US rights stop at the border. Many countries give the name to whoever
  registers first.
- **Remedies:** only a registered mark can use ®, and registration strengthens a claim for damages.

**Trademark covers only the name and logo.** The game itself (code, art, music, story) is protected
by copyright automatically.

**Plan:** launch on common-law rights if needed, and keep proof of first use:
- dated screenshots of the Steam page;
- the release date;
- sales records;
- social media posts.

Register once the game shows traction or before spending real money on marketing. The middle
option is an "intent to use" application before launch, which reserves your filing date for a few
hundred USD per category (usually "downloadable game software"). If the game takes off, an hour
with a trademark attorney is worth it.

## Financials spreadsheet

Paste this into Claude in Excel to build the workbook. Then log the Steam Direct fee there when it's
paid, plus anything already spent on the game this year (Unity, assets, the LLC filing fee).

```
I run a one-person US LLC (a single-member LLC, taxed as a "disregarded entity", so the business
shows up on my personal return on Schedule C). The business makes and sells a video game,
Bonsaimancer, which will be sold on Steam. I'm new to running a business and to bookkeeping, so
please build a simple, beginner-friendly workbook I can keep up to date myself. Use formulas, not
typed-in totals, so everything updates when I add rows.

Build these sheets:

1. "Start Here": a short plain-English guide: what each sheet is for, how to add a transaction,
   and a reminder to save receipts and keep business and personal money separate.

2. "Transactions": the main log, one row per money movement, as an Excel Table so new rows
   pick up formulas and dropdowns automatically. Columns:
   - Date
   - Description
   - Vendor / Payer
   - Type (dropdown: Income, Expense, Owner Contribution, Owner Draw)
   - Category (dropdown from the Categories sheet)
   - Amount (always positive; Type decides whether it's money in or out)
   - Signed Amount (formula: positive for Income and Owner Contribution, negative for Expense
     and Owner Draw)
   - Paid From / Into (dropdown: Business Checking, Business Card, Personal - reimburse)
   - Receipt (a link or file name)
   - Notes
   Freeze the header row, use currency format, and add 3–4 example rows clearly marked
   "EXAMPLE - delete me".

3. "Categories": a list of expense and income categories, each mapped to the Schedule C line it
   belongs to, plus a one-line plain-English description. Include at least:
   - Steam sales (net payouts)
   - Other game income
   - Software & subscriptions (Unity, tools, AI tools)
   - Game assets & licenses (art, music, sound, fonts)
   - Hardware & equipment (note: large purchases may need depreciation; see the Assets sheet)
   - Platform & store fees (e.g. the Steam Direct fee)
   - Advertising & marketing
   - Legal & professional fees
   - State filing fees & business licenses
   - Bank fees
   - Website & domain
   - Education & books
   - Travel (e.g. game events)
   - Office supplies
   - Contractors (artists, testers, musicians)
   The Category dropdown on Transactions should read from this list, so I can add categories later.

4. "Steam Payouts": one row per monthly Steam payout. Columns:
   - Month
   - Gross sales
   - Refunds/chargebacks
   - Sales tax/VAT withheld by Steam
   - Steam's share (default 30%, as an editable input cell)
   - Net payout received
   - Units sold
   - Notes
   Add a running total, and a check column comparing "Net payout" to the matching Income rows
   on Transactions.

5. "Assets": equipment and software over about $500 that I might depreciate. Columns:
   - Item
   - Date bought
   - Cost
   - Business-use %
   - Business portion (formula)
   - Notes
   Add a note that my accountant decides how it's deducted.

6. "Monthly Summary": a grid of months (Jan–Dec) by category, using SUMIFS on Transactions.
   Include total income, total expenses, net profit, owner contributions and owner draws per
   month. Put the year in one input cell (default 2026) so I can copy the workbook for future
   years.

7. "Tax Summary": a year total per Schedule C line from the Categories mapping, plus net profit.
   Add a "set aside for taxes" estimate: net profit × an editable rate cell (default 30%), and a
   note that this is a rough savings guide, not tax advice, and that quarterly estimated taxes
   may apply once there is profit.

General:
- Keep the styling clean and readable: a header style, light banding and consistent currency
  format. Use one colour for input cells I can change.
- Protect formula cells from accidental edits, but don't password-lock anything.
- After building, briefly explain how to log: a Steam payout, a software subscription, money I
  put into the business from my personal account, and money I take out for myself.
```
