# Software Install Workflow — Test Scenarios

## Prerequisites
- Admin portal running with seeded data (software-install-sub workflow, approved-software-catalog)
- Agent running and connected to ServiceNow PDI
- Windows tool server running with ad-computer-lookup and remote-software-install capabilities
- Test accounts in montanifarms.com AD domain

## Scenario 1: Happy Path — Complete Information

**Ticket**:
- Caller: Luke Skywalker (luke.skywalker)
- Short Description: "Install 7-Zip on my workstation"
- Description: "Please install 7-Zip on workstation DESK-LSKYWALKER. I need it for extracting archive files."

**Expected Flow**:
1. Dispatcher classifies as `software-install` (high confidence)
2. Routes to software-install-sub workflow
3. Query user info — gets Luke's AD account details
4. Classify request — extracts software_name="7-Zip", computer_name="DESK-LSKYWALKER"
5. Resolve software — matches "7-Zip" in catalog (confidence >= 0.7)
6. Resolve computer — "DESK-LSKYWALKER" identified (confidence >= 0.7)
7. Lookup computer — found in AD, reachable
8. Validate — all checks pass
9. Approve — auto-approved (confidence > 0.95)
10. Execute — `choco install 7zip -y` on DESK-LSKYWALKER
11. Notify — "7-Zip has been installed on DESK-LSKYWALKER"
12. End — ticket resolved

**No clarification needed** — all information provided upfront.

## Scenario 2: Clarification Needed — Missing Computer Name

**Ticket**:
- Caller: Leia Organa (leia.organa)
- Short Description: "Need Visual Studio Code"
- Description: "I'm starting a new project and need VS Code installed. Thanks!"

**Expected Flow**:
1. Dispatcher classifies as `software-install`
2. Routes to software-install-sub workflow
3. Query user info — gets Leia's AD account details
4. Classify request — software_name="Visual Studio Code", computer_name=unknown
5. Resolve software — matches "Visual Studio Code" in catalog
6. Resolve computer — confidence < 0.7 (no computer name found)
7. **Clarify computer** — Posts to ticket: "I can help install Visual Studio Code for you. Could you please provide the name of the computer where you'd like it installed?"
8. Ticket state — "awaiting_info"
9. **SUSPENDED** — waiting for user reply
10. Leia replies: "It's DESK-LORGANA"
11. Agent detects reply, resumes workflow
12. Lookup computer — found in AD
13. Validate — passes
14. Approve — approved
15. Execute — install
16. Notify — success
17. End — resolved

**One clarification round** — computer name missing.

## Scenario 3: Security Denial — Unauthorized Software

**Ticket**:
- Caller: Han Solo (han.solo)
- Short Description: "Install BitTorrent"
- Description: "I need a BitTorrent client on DESK-HSOLO for downloading large files."

**Expected Flow**:
1. Dispatcher classifies as `software-install`
2. Routes to software-install-sub workflow
3. Query user info — gets Han's AD account details
4. Classify request — software_name="BitTorrent", computer_name="DESK-HSOLO"
5. Resolve software — no catalog match (confidence < 0.7)
6. **Clarify software** — Posts catalog options to ticket
7. Han replies: "I specifically need BitTorrent, not any of those"
8. Agent detects reply, resumes
9. Resolve software again — still no match
10. **Escalate** — "Requested software not in approved catalog"
11. End — escalated

**Denied** — software not in approved catalog, escalated to human.

## Test Accounts Reference

| User | SAM Account | Workstation | Laptop |
|------|-------------|-------------|--------|
| Luke Skywalker | luke.skywalker | DESK-LSKYWALKER | LAPTOP-LSKYWALKER |
| Han Solo | han.solo | DESK-HSOLO | — |
| Leia Organa | leia.organa | DESK-LORGANA | — |
| Obi-Wan Kenobi | obi-wan.kenobi | DESK-OKENOBI | — |

## Approved Software Catalog

| Package | Display Name | Install Command |
|---------|-------------|-----------------|
| chrome | Google Chrome | `choco install googlechrome -y` |
| firefox | Mozilla Firefox | `choco install firefox -y` |
| vscode | Visual Studio Code | `choco install vscode -y` |
| 7zip | 7-Zip | `choco install 7zip -y` |
| notepadplusplus | Notepad++ | `choco install notepadplusplus -y` |
| putty | PuTTY | `choco install putty -y` |
| adobereader | Adobe Acrobat Reader | `choco install adobereader -y` |
| vlc | VLC Media Player | `choco install vlc -y` |
