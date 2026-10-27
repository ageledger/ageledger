# Retention of age-category data and the decision log

Not legal advice. This page is background research and a design proposal. It does not say what binds any app, and nothing here means that using the library, or any setting of it, makes an app compliant with a law.

Date checked: 2026-10-25. Code described: the library as of 2026-10-25. Implemented on 2026-10-25: the `AgeBucketLogging` option, the `ClearAsync()` removal of orphan temporary files, and the tests listed under "Tests". The "Proposal" section below is kept as the design record; where it says "proposed" the item is now implemented unless stated. Where this page says "today", it means the code as of that date.

Update 2026-10-25 (log format step): the second option value was renamed from `Unknown` to `Withheld`, and the format has a new `ageBucket` value, `withheld`. `withheld` means a range was received and the app chose not to record it; `unknown` now means only that no range was received. The earlier limitation that a reader "cannot tell the two apart" is resolved. Where this page still says that `Withheld` "writes `unknown`", read it as: `withheld` when a range was received, `unknown` when none was. The log format itself is defined in `docs/decision-log-format.md`.

## Summary

- The decision log stores `ageBucket` (for example `13-15`) in every entry. Several US app-store age laws restrict what a developer may do with the "age category" the store provides.
- Texas Bus. & Com. Code 121.055(b) is the only text found that has a developer deletion duty: the developer "shall delete personal data provided by the owner of an app store under Section 121.024 on completion of the verification required by Section 121.054." Utah, Louisiana, Alabama and California have use and sharing limits on developers, but no deletion duty or retention period for developers in the texts read.
- The text does not settle whether a bucket string stored next to a decision, with no identifier, is "personal data" or is "provided ... under Section 121.024". That is a question for counsel. The text is also silent about copies or derivatives a developer creates.
- Smallest change proposed: one new option, `AgeSignalsOptions.AgeBucketLogging`, with two values, `Exact` (today's behaviour, the default) and `Withheld` (the log writes `withheld` in `ageBucket` when a range was received, `unknown` when none was). No change to the five keys or the file format; the validation pattern gained the value `withheld`. The app chooses; the library never picks by jurisdiction.
- Coarsening (for example `0-17` / `18+`) is not recommended as a retention measure. A coarse bucket is still information about the user's age category. Only `withheld` and `unknown` remove it.
- The `decision` field can itself reveal the category. For a `Shared` result under the default policy, `Blocked` means the lower bound was under 13, `AllowedWithParentalConsent` means 13 to 17, `Allowed` means 18 or older. Logging `ageBucket` as `withheld` removes the explicit bucket, not that inference. An app that must keep no trace of the category at all should use no log (option c), or accept the inference.
- Decided: the default value is `Exact`; an app opts in to `Withheld`. See "Decided" under "Open questions".
- Platform terms (checked 2026-10-25, see "Platform terms"): none of the Apple or Google texts read has a rule on storing, caching or retaining the age signal, or a deletion duty. Both limit what the data may be used for (Google: "the sole purpose of complying with applicable legal and regulatory obligations"; Apple: "age-appropriate content and/or features ... or to comply with laws") and both bar passing it on (Google: to any third party except as required by law; Apple: no sharing or selling to data brokers or information resellers). A local log that is not exported or sent anywhere is not addressed by those words either way; what an app does with `ExportJsonAsync()` output is the app's matter and is where those limits bite. Whether a compliance record is within each platform's permitted purpose is not stated.
- `ruleId` should name the rule (for example `gate-v1`), never an age group, because a rule id such as `under13-flow` would leak the category the bucket setting is meant to withhold.

## Law findings

All quotations are from the enacted or enrolled text, copied on 2026-10-25. "verified" means read in the primary source on that date. "secondary" means seen only in a mirror or summary. "unverified" means not read.

| State and act | Section | Quote | What it asks of developers | Label | Link | Date checked |
|---|---|---|---|---|---|---|
| Texas, SB 2420 (2025), Bus. & Com. Code ch. 121 | 121.002(1) | "'Age category' means information collected by the owner of an app store to designate a user based on the age categories described by Section 121.021(b)." | Definition only. The four categories are child, younger teenager, older teenager, adult. | verified | [enrolled text](https://capitol.texas.gov/tlodocs/89R/billtext/html/SB02420F.htm) | 2026-10-25 |
| Texas | 121.002(5) | "'Personal data' means any information, including sensitive data, that is linked or reasonably linkable to an identified or identifiable individual." (the definition continues: it includes pseudonymous data in stated circumstances and excludes "deidentified data or publicly available information") | Definition that decides what 121.055(b) covers. | verified | same | 2026-10-25 |
| Texas | 121.024 | "The owner of an app store ... shall, using a commercially available method, allow the developer of a software application to access current information related to: (1) the age category assigned to each user under Section 121.021(b); and (2) whether consent has been obtained for each minor user under Section 121.022." | Defines what the store provides, and so what 121.055 refers to. | verified | same | 2026-10-25 |
| Texas | 121.054(a) | "The developer of a software application shall create and implement a system to use information received under Section 121.024 to verify: (1) for each user ..., the age category ...; and (2) for each minor user ..., whether consent has been obtained under Section 121.022." | The verification whose completion triggers 121.055(b). "Verify" and "completion" are not defined in the chapter. | verified | same | 2026-10-25 |
| Texas | 121.055(a), (b) | "(a) The developer of a software application may use personal data provided to the developer under Section 121.024 only to: (1) enforce restrictions and protections on the software application related to age; (2) ensure compliance with applicable laws and regulations; and (3) implement safety-related features and default settings. (b) The developer of a software application shall delete personal data provided by the owner of an app store under Section 121.024 on completion of the verification required by Section 121.054." | Use limit, and a deletion duty with no stated exception and no time limit. Subsection (b) does not refer to (a)(2). | verified | same | 2026-10-25 |
| Texas | 121.056(a)(3) | "... the developer of a software application violates this subchapter if the developer: ... (3) shares or discloses the personal data of a user that was acquired under this subchapter." | No sharing or disclosure of the data. | verified | same | 2026-10-25 |
| Texas | 121.056(c) | "The developer of a software application is not liable for a violation of Section 121.054 if the software developer: (1) relied in good faith on age category and consent information received from the owner of an app store; and (2) otherwise complied with the requirements of this section." | Safe harbour for the verification duty only. It does not name 121.055 (use and deletion). It does not mention records or evidence. | verified | same | 2026-10-25 |
| Texas | 121.025(1)(C) | The store "shall protect the personal data of users by: (1) limiting the collection and processing of personal data to the minimum amount necessary for: ... (C) maintaining compliance records". | Binds the app store, not the developer, by its words. Noted because it is the only "compliance records" wording in the chapter. | verified | same | 2026-10-25 |
| Texas | Bill Sec. 3 | "This Act takes effect January 1, 2026." | Text states only the date. Whether it could be enforced on a given date depends on court orders; court status was not checked from primary sources. | text verified; court status not checked | same | 2026-10-25 |
| Utah, S.B. 142 (2025) as amended by H.B. 498 (2026), Utah Code ch. 13-76 (numbered 13-75 in the 2025 act) | 13-76-101(3) | "'Age category data' means information about a user's age category that is: (a) collected by an app store provider; and (b) shared with a developer." | Definition. | verified | [H.B. 498 enrolled](https://le.utah.gov/Session/2026/bills/enrolled/HB0498.pdf); [S.B. 142 enrolled](https://le.utah.gov/Session/2025/bills/enrolled/SB0142.pdf) | 2026-10-25 |
| Utah | 13-76-202(4)(c), (5) | "(4) Beginning May 6, 2027, a developer may not: ... (c) share age category data with any person. (5) Beginning May 6, 2027, a developer may only use age category data received through the app store's data sharing methods to: (a) enforce any developer-created age-related restrictions; (b) ensure compliance with applicable laws and regulations; or (c) implement any developer-created safety-related features or defaults." | No sharing; use limited to three purposes. No deletion duty and no retention period for developers in the bill text. (The 2025 act had the use limit at 13-75-202(1)(c), without "only".) | verified | H.B. 498 enrolled | 2026-10-25 |
| Utah | 13-76-404(4) | "Nothing in this chapter shall be construed to: ... (4) require a developer to collect, retain, reidentify, or link any information beyond what is: (a) necessary to verify age categories and parental consent status as required by this chapter; and (b) collected, retained, reidentified, or linked in the developer's ordinary course of business". | The chapter does not require more collection or retention. It does not say a developer may not keep more. | verified | H.B. 498 enrolled | 2026-10-25 |
| Utah | Bill Sec. 8 | "This bill takes effect: (1) except as provided in Subsection (2), May 6, 2026; or (2) if approved by two-thirds of all members elected to each house: (a) upon approval by the governor ..." The developer duties above are tagged "Beginning May 6, 2027". Whether the two-thirds vote happened was not checked. | Operative date of the use and sharing limits. | verified (text) | H.B. 498 enrolled | 2026-10-25 |
| Louisiana, Act 185 (HB 977, 2026), La. R.S. 51:1771-1775. It replaces Act 481 (HB 570, 2025), which Act 185 Section 1 says "shall not become effective". | 51:1771(2) | "'Age category data' means information about a user's age that is collected by a covered application store provider and shared with a developer." | Definition. | verified | [Act 185 enrolled](https://legis.la.gov/legis/ViewDocument.aspx?d=1475238) | 2026-10-25 |
| Louisiana | 51:1773(C)(1), (D)(3) | "(C)(1) A developer, other than one required to verify age pursuant to R.S. 9:2800.29, pursuant to R.S. 51:1773(A)(5), shall use the verified age signal from an operating system or application store to do all of the following: (a) Enforce legally required minimum age restrictions. (b) Ensure compliance with all laws and other obligations. (c) In the provision of any age-appropriate defaults, safeguards, or experiences ..." and "(D) A developer shall not do any of the following: ... (3) Sell age category data to any person." | A use description (it does not apply to a developer required to verify age under R.S. 9:2800.29 pursuant to 51:1773(A)(5)) and a ban on selling (not on sharing). No deletion duty or retention period for developers found. | verified | Act 185 enrolled | 2026-10-25 |
| Louisiana | 51:1774(E)(4) | "Nothing in this Part shall be construed to ... (4) Require a developer to collect, retain, reidentify, or link any information beyond any of the following: (a) What is necessary to verify age categories and parental consent status as required by this Part. (b) What is collected, retained, reidentified, or linked in the developer's ordinary course of business." | Same shape as Utah's: no extra retention is required. | verified | Act 185 enrolled | 2026-10-25 |
| Louisiana | Act 185 Sec. 6 | "Sections 2, 3, 4 and this Section of this Act shall become effective on July 1, 2027." | Operative date. | verified | Act 185 enrolled | 2026-10-25 |
| Louisiana, Act 481 (HB 570, 2025) | Resume digest | Digest: "Effective July 1, 2026." Only the digest was read, not the enrolled act text. Act 185 says Act 481 does not become effective. | Superseded. Listed because HB 570 is often cited. | secondary (digest, official site) | [digest](https://www.legis.la.gov/Legis/ViewDocument.aspx?d=1429703) | 2026-10-25 |
| Alabama, HB 161 (2026) | Sec. 9(a)(3), 9(c)(3) | "(3) Limit use of age category data received from an app store provider to: (i) enforce developer-created age-related restrictions, protections, or defaults; (ii) ensure compliance with applicable laws or regulations; or (iii) implement safety-related features or defaults." and "(3) Share age category data with any person." | Use limit and no sharing. No deletion duty or retention period for developers found. Sec. 13(4) has the same "not required to retain" clause. | verified | [enrolled](https://alison.legislature.state.al.us/files/pdf/SearchableInstruments/2026RS/HB161-enr.pdf) | 2026-10-25 |
| Alabama | Sec. 15 | "This act shall become effective on January 1, 2027." | Operative date. | verified | same | 2026-10-25 |
| California, AB 1043 (2025), Civ. Code 1798.500-1798.505 | 1798.501(b)(4) | "A developer that receives a signal pursuant to this title shall use that signal to comply with applicable law but shall not do either of the following: (A) Request more information from an operating system provider or a covered application store than the minimum amount of information necessary to comply with this title. (B) Share the signal with a third party for a purpose not required by this title." | Use for compliance; no extra requests; no sharing for non-required purposes. No deletion duty or retention period found in the section read. | secondary (code mirror; official page not opened) | [mirror](https://california.public.law/codes/civil_code_section_1798.501) | 2026-10-25 |

Louisiana correction (checked 2026-10-25): an earlier version of this table said that 51:1774 limits developer sharing; that is not supported by the enrolled Act 185 text. 51:1774 is titled "Protections". Subsections A to E contain the good-faith and demonstrated-compliance protections (A), the industry-standards rule for app age category (B), a limit on the protections (C), a savings clause ("Nothing in this Part shall displace any other available remedies", D) and rules of construction (E). Read in full (A to E, from the enrolled text as extracted from the PDF), it has no sharing, selling or deletion rule for developers. Its only retention wording is E(4), quoted in the table, which says the Part does not require a developer to collect or retain more. The Louisiana limits on developers are in 51:1773(D): the only data rule is (D)(3), "Sell age category data to any person." The wording on sharing in Act 185 is on the store: 51:1772(B)(3), "Share age verification data except between a covered application store provider and a developer as required by this Part or as required by law." So the earlier table rows stand, and the Louisiana limit on developers is a ban on selling, not on sharing.

Findings:

1. Texas 121.055(b) is a plain deletion duty. Utah, Louisiana, Alabama and California limit use and sharing but, in the texts read, do not require developers to delete or set a retention period (verified for Texas, Utah, Louisiana, Alabama; secondary for California).
2. Utah and Alabama and Texas limit what a developer may do with the data (use only for listed purposes). One of the listed purposes is "ensure compliance with applicable laws and regulations". Whether keeping a decision record is within that purpose is not stated in any of the texts.
3. Utah and Alabama ban sharing age category data with any person. Louisiana bans selling it. California bars sharing the signal with a third party for a purpose the title does not require. Texas bars sharing or disclosing the personal data. The library sends nothing anywhere; the log is a local file. `ExportJsonAsync()` hands the log to the app, so what the app then does with an export is the app's matter.
4. Not read: the Texas Deceptive Trade Practices Act remedies (121.101 refers to ch. 17), any court order on any of these acts (court status was not checked from primary sources), any agency rule or guidance, and the official California page.

## Platform terms (Apple and Google)

Not legal advice. These are the developer terms found on 2026-10-25 that bear on storing or using the age signal. Same labels as above: "verified" means read in the primary source on that date, "secondary" means seen only in a summary, "unverified" means not read. The Apple agreement page says the version accepted in a developer account is the binding one; the public copy is "provided for your convenience".

| Platform and source | Section | Quote | What it says about storing, retaining, sharing or using the signal | Label | Link | Date checked |
|---|---|---|---|---|---|---|
| Apple, Developer Program License Agreement (page footer: agreement last updated 2026-10-13) | 3.3.3(O) | "Your Application may access the Declared Age Range API to request a user's age range only for the purposes of providing age-appropriate content and/or features for end users in Your Application, or to comply with laws that require Your Application to receive age range information." and "You may not share or sell user data obtained from this API to data brokers or information resellers." | Use limit (two purposes) and a ban on sharing or selling to data brokers and information resellers. Nothing on storage, retention or deletion. Sharing with other kinds of recipients is not addressed here. | verified | [agreement](https://developer.apple.com/support/terms/apple-developer-program-license-agreement/) | 2026-10-25 |
| Apple, same | 3.3.3(O) | "You are solely responsible for ensuring compliance with associated laws or regulations that may apply to You, and understand that Your use of the Declared Age Range API and Significant App Update Topic API is subject to Section 10 (Indemnification)." | Compliance is the developer's. Indemnification applies. | verified | same | 2026-10-25 |
| Apple, same | 3.3.3(A) | "any form of data, content or information collection, processing, maintenance, uploading, syncing, storage, transmission, sharing, disclosure or use performed by, through or in connection with Your Application must comply with all applicable privacy laws and regulations as well as any related Program Requirements, including but not limited to any notice or consent requirements." | General: storage is allowed if privacy law and program requirements are met. It points back to the law, so the state-law findings above are what decides. | verified | same | 2026-10-25 |
| Apple, same | 3.3.3(B) | "You and Your Applications ... may not collect user or device data without prior user consent, whether such data is obtained directly from the user or through the use of the Apple Software, Apple Services, or Apple SDKs, and then only to provide a service or function that is directly relevant to the use of the Application ... You may not broaden or otherwise change the scope of usage for previously collected user or device data without obtaining prior user consent". | General. The Declared Age Range request itself shows a system consent sheet (below); whether keeping a record is "directly relevant" is not stated. A new use of stored data (for example analytics) would need new consent. | verified | same | 2026-10-25 |
| Apple, Declared Age Range framework documentation, "Requesting people share their age range with your app" | Article text | "When you request an age range, the system presents a sheet that describes the data you're requesting and asks people to grant permission to share their age range with your app." and "Data from the Declared Age Range API is based on information declared by a person, or their parent or guardian. You are solely responsible for ensuring compliance with associated laws or regulations that may apply to your app." | Sharing with the app is by the person's permission (in some regulated regions the system provides it without a choice, per the same article). No sentence on what the app may store or for how long. The article says the system caches responses, which is about Apple's side, not the developer's. | verified | [article](https://developer.apple.com/documentation/declaredagerange/requesting-people-share-their-age-range-with-your-app) (read through the documentation JSON feed of the same page) | 2026-10-25 |
| Apple, Declared Age Range landing page and entitlement page | Overview | "Data from the Declared Age Range API is based on information declared by a person, or their parent or guardian, and may be confirmed using a method like a credit card or government ID. You are solely responsible for ensuring compliance with associated laws or regulations that may apply to your app." The entitlement page only describes how to enable the capability. | No storage, retention or sharing terms. | verified | [framework](https://developer.apple.com/documentation/declaredagerange); [entitlement](https://developer.apple.com/documentation/bundleresources/entitlements/com.apple.developer.declared-age-range) | 2026-10-25 |
| Apple, Age assurance support page | FAQ | "For questions about your compliance obligations, consult your legal counsel." | No storage, retention or sharing terms; it says the API shares the age category "when called" in regions where law requires a check. | verified | [page](https://developer.apple.com/support/age-assurance/) | 2026-10-25 |
| Apple, App Review Guidelines (page says last updated 2026-10-16) | 5.1.1(i), (iii) | "Explain its data retention/deletion policies and describe how a user can revoke consent and/or request deletion of the user's data." and "Apps should only request access to data relevant to the core functionality of the app and should only collect and use data that is required to accomplish the relevant task." | An app that stores the bucket needs its privacy policy to say what it keeps and for how long (the first sentence is about all data the app collects). Minimisation applies. No rule on the age range in particular: the words "Declared Age Range" and "age assurance" do not appear in the guidelines text read. | verified | [guidelines](https://developer.apple.com/app-store/review/guidelines/) | 2026-10-25 |
| Apple, App Review Guidelines | 5.1.2(i), (ii), (iii) | "Unless otherwise permitted by law, you may not use, transmit, or share someone's personal data without first obtaining their permission." "Data collected for one purpose may not be repurposed without further consent unless otherwise explicitly permitted by law." "Apps should not attempt to surreptitiously build a user profile based on collected data ..." | General limits on use, sharing and repurposing of personal data. They apply to the bucket only if it is personal data, which the guidelines do not say. | verified | same | 2026-10-25 |
| Apple, App Review Guidelines | 1.3 (Kids Category) | "In addition, Kids Category apps may not send personally identifiable information or device information to third parties." | Matters only for apps in the Kids Category. The log has no identifiers and is local. | verified | same | 2026-10-25 |
| Google, Play Age Signals API overview, terms of service (last modified October 2025) | Terms 2, 3 | "You may only use information from the Play Age Signals API to provide age-appropriate content and experiences in compliance with laws. You may not use the Play Age Signals API for any other purpose including, but not limited to, advertising, marketing, user profiling, or analytics." and "Use of the Play Age Signals API is limited to apps that are updated by Google Play and the information returned by the API can be used only by the requesting app." | Purpose limit with analytics and profiling named as barred. "Can be used only by the requesting app" reads as a limit on who uses the information; it does not mention storage. | verified | [overview](https://developer.android.com/google/play/age-signals) | 2026-10-25 |
| Google, same page | Terms 4 | "Use of the Play Age Signals API for a prohibited purpose may result in termination of your API access and suspension or takedown of your apps from Google Play." | Consequence of breaking the purpose limit. | verified | same | 2026-10-25 |
| Google, same page | Data safety | "No data is collected by the Play Age Signals client library." and "You are solely responsible for deciding how to respond to Google Play's data safety section form regarding your app's user data collection, sharing, and security practices." The page lists "Not applicable" for data sharing and deletion (about the client library). | The library collects nothing; what the app stores is for the app's own Data safety answers. | verified | same | 2026-10-25 |
| Google, Play policy "Permissions and APIs that Access Sensitive Information", section "Age Signals API and User Data Policy" (article shown with a notice that changes are coming; no effective date was given for this section in the text read) | Full Policy | "You may only use the data accessed via the Age Signals API for the sole purpose of complying with applicable legal and regulatory obligations such as providing age appropriate experiences within your app." and, among prohibited uses, "Data analytics, user profiling, or business intelligence" and "Selling, sharing, or transferring the data to any third party for any reason, except as strictly required by law". | Purpose limit: legal and regulatory compliance. Sharing or transferring to any third party is barred except as strictly required by law. Nothing on storage, caching or retention. The article's disclaimer says the full policy prevails over its summaries. | verified | [policy article](https://support.google.com/googleplay/android-developer/answer/16585319) (a preview article; the same section was found by the page text, not the live Play Console copy) | 2026-10-25 |
| Google, Play User Data policy and Families policy articles | Whole articles | Searched the text of both for "age signals": no match. | Neither says anything about the signal. The Families policy was not read for other points. | verified (negative search; policy text not otherwise analysed) | [User Data](https://support.google.com/googleplay/android-developer/answer/10144311); [Families](https://support.google.com/googleplay/android-developer/answer/9893335) | 2026-10-25 |
| Google, Developer Distribution Agreement and Google APIs Terms of Service | Whole documents | Linked from the API terms. Not opened. | Unknown. | unverified | [DDA](https://play.google/developer-distribution-agreement.html); [API ToS](https://developers.google.com/terms) | 2026-10-25 |

Findings:

1. No platform text found says anything about storing, caching or retaining the age signal, and none sets a retention period or a deletion duty. On the words read, storing a bucket in a local log is neither allowed nor barred by name.
2. Both platforms limit purpose, and both name compliance with law as a permitted purpose: Google, "the sole purpose of complying with applicable legal and regulatory obligations" (and the API terms add "to provide age-appropriate content and experiences in compliance with laws"); Apple, "providing age-appropriate content and/or features ... or to comply with laws that require Your Application to receive age range information". Whether keeping a decision record is within that purpose is not said. A record of how the app applied the signal is a plausible reading of "comply", but neither text says so.
3. Sharing: Google bars sharing or transferring the data to any third party "except as strictly required by law", and the API terms say the information "can be used only by the requesting app". Apple's agreement bars sharing or selling to "data brokers or information resellers" only, plus the general consent rules in the guidelines. The library sends nothing anywhere. An app that sends `ExportJsonAsync()` output with a bucket to a backend, crash reporter, analytics tool or other service takes on those limits, and analytics and profiling are barred uses on both platforms in the texts read (Google by name; Apple through 5.1.2(ii) and (iii) if the bucket is personal data).
4. The deletion rule that exists is Texas 121.055(b) (above). It is not a platform rule.

### What this means for the proposal

- Does storing the bucket under `Exact` conflict with a term? Not on the words found. No platform text bars local storage or sets a retention period. The limits that could apply are on purpose and onward sharing. A local log kept as a compliance record, never exported to analytics or a third party, is not what those sentences name. Whether a record is a permitted purpose is open (counsel question 6), and whether a record helps as evidence is a question for counsel (question 4).
- Does `Withheld` help? With respect to the platform terms, somewhat: with no bucket in the log there is less age data in anything an app later exports or backs up, so the sharing and analytics limits have less to bite on. It does not remove the inference from `decision` (see "Does the decision field reveal the category?"), and the platform texts do not require it.
- Apple's 5.1.1(i) is a disclosure point: an app that keeps the log should say in its privacy policy what it keeps and for how long. The library's `retention` and `maxEntries` settings are what describe that. Google's Data safety form is likewise the app's own answer.
- Recommendation change: none to the option or the default. Two documentation additions: the README text should warn that exports are subject to the platform limits on sharing and on analytics use (see the updated README text below), and the retention and cap settings of the log should be stated in the app's privacy policy. No code change follows from the platform terms.

## Why the log matters here

What the library writes today: `DecisionLogEntry.From` builds the entry with `result.Bucket.Label` as `ageBucket`. The label is one of `unknown`, `N+` or `N-M`. The service writes the entry right after the policy decision, so the row is written at the moment verification, in the app's sense, is done.

What the library does not keep: no install ID, source tier, account ID or other identifier is in the model or the log. The five-field row has no user identifier. Whether a row is "linked or reasonably linkable" to an individual depends on what else the app holds on the same device and how that is judged, which the text does not say.

Open reading points (for counsel, not decided here):

- Is a developer-written bucket string next to a decision "personal data provided by the owner of an app store under Section 121.024"? The definition of "age category" is tied to information "collected by the owner of an app store". Chapter 121 does not say whether a developer's own copy, summary or label is covered.
- Does "completion of the verification" mean the moment the app decides, so that nothing may be kept afterwards? "Verify" and "completion" are not defined, and there is no number of days.
- Is a decision record that does not contain the category within the allowed use "ensure compliance with applicable laws and regulations"?
- 121.056(a)(3) prohibits sharing or disclosing personal data "acquired under this subchapter". The word "subchapter" there means Subchapter C (developers), while the store's access duty, 121.024, is in Subchapter B. The enrolled text does not say how the two connect, and this page does not interpret it.

## Does a coarse bucket help?

Only partly, and the texts do not say it helps.

- Texas defines "age category" as information designating a user by one of four categories. A bucket such as `0-17` or `18+` is a coarser statement of the same kind of information. Nothing in 121.055 distinguishes fine from coarse, so it would be a mistake to assume coarsening takes the value outside the duty. Utah, Louisiana, Alabama and California use "age category data" or "signal" language that likewise does not carve out coarse values.
- `withheld` and `unknown` are the only values that carry no age range. (The first draft used `unknown` for both; `withheld` was added so a reader can tell redaction from no signal.)
- Coarsening has a legitimate data-minimisation value (a smaller set of values to hold), but it is not a deletion measure. If wanted, it is a separate feature (see "Alternatives").

## Does the decision field reveal the category?

Yes, to a degree that depends on the app's policy.

- For a `Shared` result under the default policy: `Blocked` follows a lower bound below `MinimumAge` (13), `AllowedWithParentalConsent` follows 13 to 17, `Allowed` follows 18 or older. A reader of the log who knows the options can infer a coarse category from the decision.
- `Blocked` is also what `VerificationRequiredDecision` returns by default, and `NoSignalDecision` (default `NotApplicable`) covers no-signal cases. So the inference is not exact, but for a `Shared` result it is direct.
- A custom `SharedRangePolicy` changes the mapping but, for any policy that treats age groups differently, the decision still carries information about the group.
- For a `withheld` row the inference is exact under the default policy, because `withheld` is only written for a received range (a row with `unknown` carries no such information).
- Therefore `AgeBucketLogging.Withheld` removes the explicit bucket and leaves this inference. If an app's lawyers conclude that any trace of the category must go, the library offers only one answer: do not pass a log (option c). A decision record that says "Allowed" for every user would be a record that says nothing, and the library will not fake decisions.

## Does deletion "after verification" conflict with a decision record at all?

Possibly, depending on the reading:

- On a narrow reading, 121.055(b) covers the store-provided category (and consent status) and not a record of the app's own decision. A row with `unknown`, a decision, a version and a rule id then contains no store-provided data other than what the decision implies.
- On a wide reading, anything derived from the category, including the decision, must go on completion of verification. Then no per-decision record that carries the decision is possible, and only option (c) fits.
- The text does not choose. The library therefore should not choose either: it gives the app the means to keep a record without the bucket and says plainly what remains.
- The consent status is also "personal data provided ... under Section 121.024". The library does not log it (there is no field for it), so this proposal does not change anything for it.

## Design record (2026-10-25): proposal (smallest change)

### Option

Add one option, read each time an entry is built:

```csharp
public enum AgeBucketLogging
{
    /// Today's behaviour: the reported range label (for example "13-15", "18+") is logged.
    Exact = 0,
    /// "withheld" is logged in ageBucket when a range was received, "unknown" when none was.
    Withheld = 1,
}

public sealed class AgeSignalsOptions
{
    /// What the decision log stores in the ageBucket field. Default Exact. The app chooses; the library
    /// does not pick a value for any jurisdiction. Decisions are still logged, and the decision itself can imply the group.
    public AgeBucketLogging AgeBucketLogging { get; set; } = AgeBucketLogging.Exact;
}
```

In `DecisionLogEntry.From`, fail closed:

```csharp
var bucket = options.AgeBucketLogging switch
{
    AgeBucketLogging.Exact => result.Bucket.Label,
    _ => result.Bucket.IsKnown ? "withheld" : "unknown",   // Withheld, and an undefined value, never log the real bucket
};
```

Recommendation: an undefined value falls to `"withheld"` (`"unknown"` when no range was received) rather than throwing. The setting exists to keep data out of the log, so the safe failure is to log less. Throwing would also be safe (the entry is built inside the `try` in `EvaluateAsync`, so `Logged` would be false), but it would drop the decision record entirely for what is a settings mistake. Either way the real bucket is never written for an undefined value.

In the `AgeSignalsService` constructor: throw `ArgumentException` if `Enum.IsDefined(options.AgeBucketLogging)` is false, as it does for the other options, so a bad value fails at start-up. A bad value set after construction is covered by the fail-closed `switch` above.

Why options-level and not per call: `EvaluateAsync()` has no parameters today, and the setting describes how the app uses the log, not one request. A per-call setting was suggested; the options-level setting achieves the same by using one service and options instance per setting (and per `RuleId`), all sharing one log instance:

```csharp
var log = new JsonFileDecisionLog(path);
var exact   = new AgeSignalsService(new AgeSignalsOptions { Enabled = true, RuleId = "gate-v2", AgeBucketLogging = AgeBucketLogging.Exact   }, provider, log);
var unknown = new AgeSignalsService(new AgeSignalsOptions { Enabled = true, RuleId = "gate-v1", AgeBucketLogging = AgeBucketLogging.Withheld }, provider, log);
```

Do not change an options instance between calls. `EvaluateAsync` reads the options after the platform `await`, so a change made while a call is in flight can race with it; the value at log time wins. `JsonFileDecisionLog` instances on one path share one lock, so several services can share one file.

### The zero-change route, and why add the option anyway

Today an app can already do this with no library change: wrap `IDecisionLog` in its own class whose `AppendAsync` rebuilds each entry with `ageBucket` set to `"unknown"` before passing it on. The option is still worth adding because it is discoverable (it is in the options type and the README), it is documented with its limits, and it is validated and tested in one place instead of in each app's wrapper.

### Meaning of `unknown` for the format spec

Resolved 2026-10-25. `ageBucket = "unknown"` means no range was received (`Declined`, `NotShared`, `Unavailable`, `Error`, or a `Shared` result without a range). `ageBucket = "withheld"` means a range was received and the app chose not to record it. A reader can now tell redaction from no signal. A received platform range that cannot be written in the allowed forms (equal bounds, a bound of 100 or more, lower above upper) is written as `withheld` in every mode, because `unknown` means strictly "no range received" (changed 2026-10-25 from the first version, which wrote `unknown`). The first draft of this page (one value for both cases, and the limitation that a row could not show which) is kept above only as the record. The format is in `docs/decision-log-format.md`.

### Exact value

The literal strings are `unknown` and `withheld`. The pattern is now `BucketPattern` (`^(unknown|withheld|(?<lo>[0-9]{1,2})-(?<hi>[0-9]{1,2})|[0-9]{1,2}\+)\z`), `ValidateAgeBucket`, the JSON reader and `FromFields`. The library writes `unknown` for results with no known range and `withheld` for a received range under the `Withheld` setting.

### Validation change

The pattern accepts `withheld` (added 2026-10-25); the five keys, the entry converter, the file format and reading old files are otherwise unchanged. A file can contain all kinds of rows (older `13-15` rows and newer `withheld` rows).

### Interaction with retention (`MaxAge`, `MaxCount`) and clearing

- The constructor parameters of `JsonFileDecisionLog` are `maxEntries` (default 1000) and `retention` (default 365 days); they correspond to the usual "max age" and "max count" caps. They cap how long and how many rows are kept. They are not a deletion-on-completion measure: a row with a bucket stays until it ages out or is pushed out by count.
- How the caps act on the file: an expired row is hidden on read (the age and count limits apply when reading), but it is physically removed from the file only on the next append (which rewrites the file) or on `ClearAsync()`. `InMemoryDecisionLog` has no age or count caps at all.
- `JsonFileDecisionLog` appends by writing a temporary file named `<path>.<guid>.tmp` and moving it over the log. A crash between those steps can leave an orphan `<path>.<32 hex>.tmp` file that contains rows (with buckets), and `ClearAsync()` removes such leftovers as well as the log file (implemented 2026-10-25).
- Switching to `Withheld` changes new rows only. Existing rows keep their buckets until they age out or the app calls `ClearAsync()`. `ClearAsync()` deletes the whole file, decisions included. There is no per-field redaction today.
- Possible follow-up, not part of this proposal: an explicit method on `JsonFileDecisionLog` that rewrites existing rows with `withheld`. It is a larger change (it modifies stored history), so it is listed as an open question.
- An app that starts with `Withheld` from its first release has no buckets in any row to purge.

### README text change as drafted on 2026-10-25 (design record; the README now has the final wording)

> `AgeSignalsOptions.AgeBucketLogging` chooses what the log stores in `ageBucket`: `Exact` (default; the reported range such as `13-15`) or `Withheld` (the literal `withheld` is written when a range was received, `unknown` when none was). The library does not choose by country or state; the app does. With `Withheld` the explicit range is not stored, but the `decision` value can still imply an age group (with the default policy, `Blocked` means a lower bound under 13). Rows already written are not changed; `ClearAsync()` deletes the whole log. If you must keep nothing derived from the age signal, pass no log to the service. Google's and Apple's developer terms limit what the age signal may be used for and bar passing it to third parties (Google, except as strictly required by law) or to data brokers and resellers (Apple), so do not send exported log rows to analytics, advertising or other outside services. Some US app-store laws limit what a developer may do with, or require deleting, age category data supplied by a store; the texts and the points that remain open are in `docs/retention.md`. An app that keeps the log should state in its privacy policy what it keeps and the `retention` and `maxEntries` settings it uses. Not legal advice; using this setting does not make an app compliant with any law.

And in the "Laws it was designed around" section, a one-line pointer to `docs/retention.md`.

## Per-state settings an app might consider (background only)

Not legal advice. This table says which setting an app might discuss with its counsel. It is not a recommendation to use or not use any setting for any law, and no setting is a claim of compliance.

| Law (read 2026-10-25) | Text that bears on the log | Setting an app might consider |
|---|---|---|
| Texas ch. 121 | Deletion duty in 121.055(b); scope for a developer-written bucket is not stated. | `AgeBucketLogging.Withheld` so that no category string is stored; or no log if the decision itself is thought to be covered. |
| Utah ch. 13-76 (from 2027-05-06 per the bill) | Use limited to three purposes; no sharing; no deletion duty in the text read. | Text bars sharing age category data with any person and limits use to three purposes. `ExportJsonAsync()` output with `Exact` contains the bucket; with `Withheld` it does not. Which setting fits the "compliance" purpose is a question for counsel. |
| Louisiana R.S. 51:1771-1775 (from 2027-07-01 per Act 185) | Use description; no selling; no deletion duty in the text read. | Text bars selling age category data (51:1773(D)(3)); it does not use the word "share" for developers. `ExportJsonAsync()` output with `Exact` contains the bucket; with `Withheld` it does not. |
| Alabama HB 161 (from 2027-01-01 per the bill) | Use limited to three purposes; no sharing; no deletion duty in the text read. | Text bars sharing age category data with any person and limits use to three purposes. `ExportJsonAsync()` output with `Exact` contains the bucket; with `Withheld` it does not. |
| Apple Developer Program License Agreement 3.3.3(O); Google Play Age Signals terms and policy (read 2026-10-25) | Purpose limits (age-appropriate experiences or legal compliance); sharing limits (Google: no third parties except as required by law; Apple: no data brokers or resellers); no storage or retention rule. | Either setting is silent on the platform texts. Whatever the setting, exported rows should not go to analytics or outside services. `Withheld` leaves less age data in anything that is exported. |
| California AB 1043 (secondary source) | Use to comply; no extra requests; no sharing for non-required purposes. | Text bars sharing the signal for a purpose the title does not require. Confirm against the official text (mirror only). |

## Alternatives rejected

- (b) A coarse value as the retention measure. If wanted, the literals that fit the current pattern with no change are `0-17` and `18+`. `under18` does not fit the pattern (it would need a new alternation, which widens the accepted set and the README must then describe it). Rejected as the proposal because coarsening is not shown by any text to take the value outside the duty (see above), it adds a second mode that looks safer than it is, and it implies a boundary (18) that the app, not the library, should choose. It would also hide the `Apple gate` versus `Play band` differences in the logged value, which makes the log less useful as a record. Can be revisited if minimisation for its own sake.
- (c) Not logging at all, as the only option. Already possible: pass no log, and `Logged` is always false. It is the answer for the strictest reading, but it means no record exists at all, so there is nothing to point to if an app needs to show good-faith reliance for a safe harbour (Texas 121.056(c); the Utah, Louisiana and Alabama safe harbours use "demonstrates"). It is not the only option because an app that needs a decision record would lose it entirely. Whether a record helps is a question for counsel (question 4).
- Choosing the setting automatically from `RuleId` or the user's location. Rejected: the library never decides which law applies, and `RuleId` is a log label, not a legal switch.
- Changing the default to `Withheld` in this proposal. `Exact` was chosen (see "Decided" under "Open questions").
- Adding or renaming fields (for example a `bucketLogged` flag). Rejected: the five-key shape must not change.
- A per-call setting. The options-level setting with one service per setting reaches the same result without a new parameter and without racing options (see above).

## Open questions

For the maintainers:

Decided (2026-10-25): the default value is `Exact`; an app opts in to `Withheld`. An app that wants no explicit bucket in the log sets `AgeBucketLogging.Withheld`. Reasons recorded: at the time of the decision (2026-10-25) the library was unreleased and had not run on a device, the bucket is the main value of a decision record, and none of the law or platform texts read bars `Exact` by name. The point that remains open is Texas 121.055(b) (counsel question 1), which the default does not resolve; the README says that `Withheld` stores no explicit range, that passing no log to the service keeps nothing derived from the age signal, and that the texts and open points are in this page.

1. Option (c) as the choice for an app: no record at all, `Logged` always false, and nothing to point to for the good-faith safe harbours. Whether the library's README should present it as an equal option or only as the strict fallback.
2. Whether to add a method that rewrites existing rows to `withheld` (history redaction) in addition to `ClearAsync()`. (`ClearAsync()` removing orphan `<path>.<32 hex>.tmp` files is implemented.)
3. Whether to offer a coarse mode (`0-17` / `18+`) for minimisation reasons only.
4. Whether the README examples should show `Withheld` as the first example.

For counsel:

1. Is a bucket string stored by the developer next to a decision, with no identifier, "personal data provided by the owner of an app store under Section 121.024"? If yes, is "completion of the verification" the moment of the decision?
2. If the bucket is covered, is the `decision` value (which can imply the group) also covered?
3. Does keeping a decision record fall under "ensure compliance with applicable laws and regulations" (Texas 121.055(a)(2); Utah 13-76-202(5)(b); Alabama Sec. 9(a)(3)(ii))?
4. For the safe harbours (Texas 121.056(c), "relied in good faith ... and otherwise complied"; Utah, Louisiana and Alabama, "demonstrates"), what kind of evidence is accepted? None of the texts mention records. Texas 121.056(c) covers 121.054 only, not 121.055.
5. Court status and enforceability of each act on the date an app ships (not checked here).
6. Is keeping a decision record within the purposes the platforms allow (Google: "the sole purpose of complying with applicable legal and regulatory obligations"; Apple: "age-appropriate content and/or features ... or to comply with laws")? Are the Developer Distribution Agreement, the Google APIs Terms of Service and the binding in-account Apple agreement the same as the public text read here (not opened or not confirmed)?

## Tests

Implemented on 2026-10-25 in `tests/Ageledger.Tests/HardeningAndRetentionTests.cs` on the plain `net10.0` test project with the existing fakes. Items 1 to 14 and 16 to 21 are covered; item 15 (the golden fixture) is covered by an inline fixture and export string in the same file, not a separate fixture file.

1. `From` with `Exact` (default) writes the reported label (existing behaviour unchanged; a regression test with `13-15`, `18+`, `unknown`).
2. `From` with `Withheld` writes `withheld` for a `Shared` result of `13-15`, `18+` and `0-12`; with no range received (for example `Declined`) it writes `unknown`; a received range that cannot be written (`7-7`) is `withheld` in every mode, `Exact` included.
3. `EvaluateAsync` with `Withheld` and a `Shared` result: the logged entry has `ageBucket == "withheld"`, `Decision` is still the policy's decision, and `Result.Bucket` returned to the caller is unchanged (the caller still gets the real result; only the log changes).
4. Default value of `AgeSignalsOptions.AgeBucketLogging` is `Exact`.
5. The service constructor throws `ArgumentException` for an undefined enum value (for example `(AgeBucketLogging)7`).
6. Fail closed: an undefined value set after construction (for example `(AgeBucketLogging)7`) never logs the real bucket; the entry is written with `withheld` (`unknown` when no range was received; or, if throwing were preferred, `Logged` would be false), and `EvaluateAsync` does not throw.
7. File round trip: a file with an old row (`13-15`) and a new row (`withheld`) reads both; the exported JSON array has exactly the five keys in both.
8. Switching the option from `Exact` to `Withheld` mid-life leaves earlier rows unchanged and writes later rows as `withheld`.
9. `ClearAsync()` after mixed rows leaves no file, and the next append writes a single row.
10. Retention interaction: with `Withheld`, age and count caps still apply as before. An expired row is not returned by a read but is still on disk after the read, and is gone after the next append (use a fake `TimeProvider`).
11. Disabled service and service with no log: unchanged (no write, `Logged` false) for both option values.
12. A test that documents the inference: with the default policy and `Withheld`, a `Shared` result for `0-12` logs `Blocked` with `withheld`. The test name and a comment state that the decision can imply the group. No test data contains real ages or identifiers.
13. `FromFields` and the JSON reader still accept `unknown` and still reject `under18`.
14. Two services (one `Exact` and `RuleId` `gate-v2`, one `Withheld` and `RuleId` `gate-v1`) sharing one `JsonFileDecisionLog` path: each row carries its own bucket mode and rule id, and no write is lost.
15. A golden fixture for other languages to reproduce. It now lives in `docs/decision-log-format.md` (JSON Lines block and export array) and `tests/Ageledger.Tests/LogFormatSpecTests.cs` checks that the C# writer and exporter produce exactly those bytes. Made-up data only.
16. `Withheld` with a custom `SharedRangePolicy`, and with the `VerificationRequired`, `Declined` and `Error` states: `ageBucket` is `withheld` for a received range and `unknown` for the states that carry none, and the decision is the policy's.
17. `InMemoryDecisionLog` with `Withheld` (and a note in the test that it has no caps).
18. After `ClearAsync()` no `<path>.<32 hex>.tmp` file remains, and a file such as `<path>.backup.tmp` is left alone (implemented; the test uses simulated orphan files).
19. `DecisionLogEntry.From` called directly with `Withheld`, without the service.
20. Optional: options changed between the platform call and the logging; the test documents that the value at log time wins.
21. `ruleId` values that name an age group are not blocked by validation today; if that is wanted, it is a separate change. The test, if added, documents the current behaviour only.

## Sources not opened or not conclusive

- Texas court orders and any appellate ruling (not opened; court status was not checked from primary sources).
- Official California leginfo page for AB 1043 (mirror used).
- Texas Bus. & Com. Code ch. 17 remedies (referenced by 121.101, not read).
- Louisiana Act 481 enrolled text (digest only; superseded by Act 185).
- Apple: the binding in-account Developer Program License Agreement (the public copy was read); the App Review Guidelines were read in full for sections 1.3, 5.1.1 and 5.1.2 only; Apple's "Age Range for Apps" support page (support.apple.com/122770) was not opened.
- Google: the Developer Distribution Agreement, the Google APIs Terms of Service, the Google Play Terms of Service, the Families policy beyond a search for "age signals", and the Play Console live copy of the Age Signals policy (a preview article was read).
