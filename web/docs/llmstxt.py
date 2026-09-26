from bs4 import BeautifulSoup


def preprocess(soup: BeautifulSoup, output: str) -> None:
    for steps in soup.select(".us-steps"):
        steps.decompose()
    for title in soup.select(".admonition-title"):
        title.name = "strong"
        title.wrap(soup.new_tag("p"))
    for summary in soup.select("details > summary"):
        summary.name = "h3"
    for cell in soup.select("td, th"):
        for text in cell.find_all(string=lambda s: "|" in s):
            text.replace_with(text.replace("|", "\\|"))
